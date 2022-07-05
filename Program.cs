using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

#if TARGETS_OSX
using tcflag_t = System.UInt64;
using speed_t = System.Int64;
#else
using tcflag_t = System.UInt32;
using speed_t = System.UInt32;
#endif

namespace ReadKey
{
    internal unsafe class Program
    {
        private const int TCSANOW = 0, STDIN_FILENO = 0;
        private const uint ECHO = 8;
        
        private static int VTIME => OperatingSystem.IsMacOS() ? 17 : 5;
        private static int VMIN => OperatingSystem.IsMacOS() ? 16 : 6;
        private static int VERASE => OperatingSystem.IsMacOS() ? 3 : 2;
        private static uint ISIG => (uint)(OperatingSystem.IsMacOS() ? 128 : 1);
        private static uint ICANON => (uint)(OperatingSystem.IsMacOS() ? 256 : 2);
        private static uint IXON => (uint)(OperatingSystem.IsMacOS() ? 512 : 1024);
        private static uint IXOFF => (uint)(OperatingSystem.IsMacOS() ? 1024 : 4096);
        private static uint IEXTEN => (uint)(OperatingSystem.IsMacOS() ? 1024 : 32768);

        private static Termios _initialSettings;

        static void Main()
        {
            byte verase = ConfigureTerminal();

            (string term, string actualPath, byte[] db) = ReadTerminalSettings();

            (string text, ConsoleKeyInfo keyInfo)[] testCases =
            {
                // Uppercase characters: Capslock vs Shift
                ("Z (uppercase) using Shift", CKI('Z', ConsoleKey.Z, ConsoleModifiers.Shift)),
                ("Z (uppercase) using Caps Lock", CKI('Z', ConsoleKey.Z)),

                // lowercase and its Ctrl/Alt permutations
                ("a (lowercase)", CKI('a', ConsoleKey.A)),
                ("Ctrl+a (lowercase)", CKI('a', ConsoleKey.A, ConsoleModifiers.Control)),
                ("Alt+a (lowercase)", CKI('a', ConsoleKey.A, ConsoleModifiers.Alt)),
                ("Ctrl+Alt+a (lowercase)", CKI('a', ConsoleKey.A, ConsoleModifiers.Control | ConsoleModifiers.Alt)),

                // simple number(s) and its Ctrl/Alt/Shift permutations
                ("1 (number one)", CKI('1', ConsoleKey.D1)),
                ("Ctrl+1 (number one)", CKI(default, ConsoleKey.D1, ConsoleModifiers.Control)),
                ("Alt+1 (number one)", CKI('1', ConsoleKey.D1, ConsoleModifiers.Alt)),
                ("Shift+1 (number one)", CKI('!', ConsoleKey.D1, ConsoleModifiers.Shift)),

                // On Linux Ctrl+2 behaves differently than Ctrl+1 (I have no idea why)
                ("2 (number two)", CKI('2', ConsoleKey.D2)),
                ("Ctrl+2 (number two)", CKI(default, ConsoleKey.D2, ConsoleModifiers.Control)), // https://github.com/dotnet/runtime/issues/802
                ("Alt+2 (number two)", CKI('2', ConsoleKey.D2, ConsoleModifiers.Alt)),
                ("Shift+2 (number two)", CKI('@', ConsoleKey.D2, ConsoleModifiers.Shift)),

                // OEM keys
                ("= (equals sign)", CKI('=', ConsoleKey.OemPlus)),
                ("Shift+'=' (equals sign)", new ConsoleKeyInfo('+', ConsoleKey.OemPlus, true, false, false)),
                ("Ctrl+'=' (equals sign)", new ConsoleKeyInfo(default, ConsoleKey.OemPlus, false, false, true)),
                ("Alt+'=' (equals sign)", new ConsoleKeyInfo('=', ConsoleKey.OemPlus, false, true, false)),

                // Escape
                ("Escape", CKI((char)27, ConsoleKey.Escape)),
                ("Shift+Escape", new ConsoleKeyInfo((char)27, ConsoleKey.Escape, true, false, false)),
                // not testing Escape + ctrl/alt as on Windows these shortcuts are used by the OS

                // Bacspace
                ("Backspace", CKI('\b', ConsoleKey.Backspace)),
                ("Ctrl+Backspace", CKI('\b', ConsoleKey.Backspace, ConsoleModifiers.Control)),
                ("Alt+Backspace", CKI('\b', ConsoleKey.Backspace, ConsoleModifiers.Alt)),
                ("Ctrl+Alt+Backspace (don't press Ctrl+Alt+Delete!)", CKI('\b', ConsoleKey.Backspace, ConsoleModifiers.Control | ConsoleModifiers.Alt)),
                ("Shift+Backspace", CKI('\b', ConsoleKey.Backspace, ConsoleModifiers.Shift)),
                
                // Delete, but without Ctrl+Alt+Delete ;)
                ("Delete", CKI(default, ConsoleKey.Delete)),

                // Functional Key
                ("F12", CKI(default, ConsoleKey.F12)),
                ("Ctrl+F12", CKI(default, ConsoleKey.F12, ConsoleModifiers.Control)),
                ("Alt+F12", CKI(default, ConsoleKey.F12, ConsoleModifiers.Alt)),
                ("Shift+F12", CKI(default, ConsoleKey.F12, ConsoleModifiers.Shift)),
                ("Ctrl+Alt+Shift+F12", CKI(default, ConsoleKey.F12, ConsoleModifiers.Control | ConsoleModifiers.Alt | ConsoleModifiers.Shift)),
                // no test cases for Ctrl+ALt+Fx as it's system shortcut that takes users to ttyX on Ubuntu

                // Home
                ("Home", CKI(default, ConsoleKey.Home)),
                ("Ctrl+Home", CKI(default, ConsoleKey.Home, ConsoleModifiers.Control)),
                ("Alt+Home", CKI(default, ConsoleKey.Home, ConsoleModifiers.Alt)),
                ("Ctrl+Alt+Home", CKI(default, ConsoleKey.Home, ConsoleModifiers.Control | ConsoleModifiers.Alt)),
                // no test cases for Shift+Home as it's terminal shortcut on Ubuntu (scroll to the top)

                // Insert
                ("Insert", CKI(default, ConsoleKey.Insert)),
                
                // Arrow key
                ("Left Arrow", CKI(default, ConsoleKey.LeftArrow)),
                ("Ctrl+Left Arrow", CKI(default, ConsoleKey.LeftArrow, ConsoleModifiers.Control)),
                ("Alt+Left Arrow", CKI(default, ConsoleKey.LeftArrow, ConsoleModifiers.Alt)),
                
                // Enter
                ("Enter", CKI('\r', ConsoleKey.Enter)),
                ("Ctrl+Enter", CKI('\r', ConsoleKey.Enter, ConsoleModifiers.Control)),
                ("Alt+Enter", CKI('\r', ConsoleKey.Enter, ConsoleModifiers.Alt)),
                ("Shift+Enter", CKI('\r', ConsoleKey.Enter, ConsoleModifiers.Shift)),
                ("Ctrl+Alt+Shift+Enter", CKI('\r', ConsoleKey.Enter, ConsoleModifiers.Control | ConsoleModifiers.Alt | ConsoleModifiers.Shift)),
            };
            (string text, ConsoleKeyInfo keyInfo)[] numericKeypadTestCases =
            {
                ("1 (number one using Numeric Keypad)", CKI('1', ConsoleKey.NumPad1)),
                ("Ctrl+1 (number one using Numeric Keypad))", CKI(default, ConsoleKey.NumPad1, ConsoleModifiers.Control)),
                ("+ (plus sign using Numeric Keypad))", CKI('+', ConsoleKey.Add)),
                ("- (minus sign using Numeric Keypad))", CKI('-', ConsoleKey.Subtract)),
                ("Home", CKI(default, ConsoleKey.Home)),
                ("Ctrl+Home", CKI(default, ConsoleKey.Home, ConsoleModifiers.Control)),
                ("Insert", CKI(default, ConsoleKey.Insert)),
            };
            
            List<(ConsoleKeyInfo keyInfo, byte[] input)> recorded = new();

            try
            {
                do
                {
                    WriteLine($"Please don't use Numeric Keypad for now. Press `y' or 'Y` if you understand.");    
                } while (!ReadAnswer());
                
                RecordTestCases(testCases, recorded);

                WriteLine(">>>>>>>>>>>> <<<<<<<<<<<<<<<<");
                WriteLine($"Do you have Numeric Keypad? If yes, please press `y' or 'Y` and use it from now. If not, press 'n'.");
                if (ReadAnswer())
                    RecordTestCases(numericKeypadTestCases, recorded);
            }
            finally
            {
                RestoreSettings();
            }

            PrintData(term, actualPath, db, verase, recorded);

            static ConsoleKeyInfo CKI(char ch, ConsoleKey key, ConsoleModifiers modifiers = 0)
                => new ConsoleKeyInfo(ch, key, (modifiers & ConsoleModifiers.Shift) != 0, (modifiers & ConsoleModifiers.Alt) != 0, (modifiers & ConsoleModifiers.Control) != 0);
        }
        private static void RecordTestCases((string text, ConsoleKeyInfo keyInfo)[] testCases, List<(ConsoleKeyInfo keyInfo, byte[] input)> recorded)
        {
            byte* inputBuffer = stackalloc byte[1024];

            foreach ((string text, ConsoleKeyInfo keyInfo) in testCases)
            {
                WriteLine($"\nPlease press {text}");
                int bytesRead = read(STDIN_FILENO, inputBuffer, 1024);
                recorded.Add((keyInfo, new ReadOnlySpan<byte>(inputBuffer, bytesRead).ToArray()));
            }
        }

        private static void WriteLine(string text)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(text + "\n");
            fixed (byte* pinned = utf8)
            {
                write(1, pinned, utf8.Length);
            }
        }

        private static bool ReadAnswer()
        {
            byte* inputBuffer = stackalloc byte[1];
            read(STDIN_FILENO, inputBuffer, 1);
            return char.ToUpper((char)inputBuffer[0]) == 'Y';
        }

        private static byte ConfigureTerminal()
        {
            fixed (Termios* pinned = &_initialSettings)
            {
                if (tcgetattr(STDIN_FILENO, pinned) == -1) throw new Exception("tcgetattr failed");
            }

            // configure the terminal the same way CLR does: https://github.com/dotnet/runtime/blob/7414af2a5f6d8d99efc27d3f5ef7a394e0b23c42/src/native/libs/System.Native/pal_console.c#L152
            Termios copy = _initialSettings;
            copy.c_lflag |= ISIG;
            copy.c_iflag &= ~(IXON | IXOFF);
            copy.c_lflag &= ~(ICANON | IEXTEN); // in contrary to CLR, enable ECHO so the users see that their input has been recorded
            // copy.c_lflag &= ~(ECHO | ICANON | IEXTEN);
            copy.c_cc[VMIN] = 1;
            copy.c_cc[VTIME] = 0;
            if (tcsetattr(STDIN_FILENO, TCSANOW, &copy) == -1) throw new Exception("tcsetattr failed");

            return _initialSettings.c_cc[VERASE];
        }
        
        private static void RestoreSettings()
        {
            fixed (Termios* pinned = &_initialSettings)
            {
                tcsetattr(STDIN_FILENO, TCSANOW, pinned);
            }
        }

        private static readonly string[] _terminfoLocations = 
        {
            "/etc/terminfo",
            "/lib/terminfo",
            "/usr/share/terminfo",
            "/usr/share/misc/terminfo",
            "/usr/local/share/terminfo"
        };

        private static (string term, string actualPath, byte[] db) ReadTerminalSettings()
        {
            string? term = Environment.GetEnvironmentVariable("TERM");
            if (string.IsNullOrEmpty(term)) return default;
            
            string? terminfo = Environment.GetEnvironmentVariable("TERMINFO");
            if (!string.IsNullOrEmpty(terminfo))
            {
                var (actualPath, db) = ReadFile(term, terminfo);
                if (db is not null) return (term, actualPath!, db);
            }

            string? home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
            {
                var (actualPath, db) = ReadFile(term, home);
                if (db is not null) return (term, actualPath!, db);
            }

            foreach (string terminfoLocation in _terminfoLocations)
            {
                var (actualPath, db) = ReadFile(term, terminfoLocation);
                if (db is not null) return (term, actualPath!, db);
            }

            return default;

            static (string? path, byte[]? db) ReadFile(string term, string directoryPath)
            {
                string linuxPath = $"{directoryPath}/{term[0]}/{term}";
                string macPath = $"{directoryPath}/{(int)term[0]:X}/{term}";
                if (File.Exists(linuxPath)) return (linuxPath, File.ReadAllBytes(linuxPath));
                if (File.Exists(macPath)) return (macPath, File.ReadAllBytes(macPath));
                return default;
            }
        }
        
        private static void PrintData(string term, string actualPath, byte[] db, byte verase, List<(ConsoleKeyInfo keyInfo, byte[] input)> recorded)
        {
            StringBuilder sb = new(1000);
            sb.AppendLine("```cs");
            sb.Append(
$@"
public class KeyMapperTests_{term.Replace('-', '_')} : KeyMapperTests
{{
    protected override string EncodingCharset => ""{EncodingHelper.GetCharset()}"";
    protected override string Term => ""{term}"";
    protected override byte Verase => {verase};
    protected override string EncodedTerminalDb => ""{Convert.ToBase64String(db)}""; // {actualPath}

    protected override IEnumerable<(byte[], ConsoleKeyInfo)> RecordedScenarios
    {{
        get
        {{
{string.Join(Environment.NewLine, recorded.Select(x => Format(x.keyInfo, x.input)))}
        }}
    }}
}}
");
            sb.AppendLine("```");

            File.WriteAllText("upload_me.md", sb.ToString());
            WriteLine("Please upload the contents of upload_me.md.");

            static string Format(ConsoleKeyInfo ki, byte[] input)
                => $"yield return (new byte[] {{ {string.Join(", ", input)} }}, new ConsoleKeyInfo({ToSource(ki.KeyChar)}, ConsoleKey.{ki.Key.ToString()}, {((ki.Modifiers & ConsoleModifiers.Shift) != 0).ToString().ToLower()}, {((ki.Modifiers & ConsoleModifiers.Alt) != 0).ToString().ToLower()}, {((ki.Modifiers & ConsoleModifiers.Control) != 0).ToString().ToLower()}));";

            static string ToSource(char ch)
                => (int)ch switch
                {
                    0 => "default",
                    >= 32 and <= 126 => $"'{ch.ToString()}'",
                    _ => $"(char){(int)ch}" // we can't print backspace and few other characters
                };
        }

        [DllImport("libc", SetLastError = true)]
        static extern int tcgetattr(int fd, Termios* p);

        [DllImport("libc", SetLastError = true)]
        static extern int tcsetattr(int fd, int optional_actions, Termios* p);

        [DllImport("libc")]
        static extern int read(int fd, byte* buffer, int byteCount);

        [DllImport("libc")]
        static extern int write(int fd, byte* buffer, int byteCount);
        
        private struct Termios
        {
#if TARGETS_OSX
            private const int NCCS = 20;
#else
            private const int NCCS = 32;
#endif
            
            public tcflag_t c_iflag;
            public tcflag_t c_oflag;
            public tcflag_t c_cflag;
            public tcflag_t c_lflag;
            public byte c_line;
            public fixed byte c_cc[NCCS];
            private speed_t __c_ispeed;
            private speed_t __c_ospeed;
        }
    }
    
    // copied from https://github.com/dotnet/runtime/blob/main/src/libraries/Common/src/System/Text/EncodingHelper.Unix.cs
    internal static class EncodingHelper
    {
        private static readonly string[] s_localeEnvVars = { "LC_ALL", "LC_MESSAGES", "LANG" }; // this ordering codifies the lookup rules prescribed by POSIX

        internal static string? GetCharset()
        {
            string? locale = null;
            foreach (string envVar in s_localeEnvVars)
            {
                locale = Environment.GetEnvironmentVariable(envVar);
                if (!string.IsNullOrWhiteSpace(locale)) break;
            }

            if (locale != null)
            {
                // Does it contain the optional charset?
                int dotPos = locale.IndexOf('.');
                if (dotPos >= 0)
                {
                    dotPos++;
                    int atPos = locale.IndexOf('@', dotPos + 1);

                    // return the charset from the locale, stripping off everything else
                    string charset = atPos < dotPos ?
                        locale.Substring(dotPos) :                // no modifier
                        locale.Substring(dotPos, atPos - dotPos); // has modifier
                    return charset.ToLowerInvariant();
                }
            }

            // no charset found; the default will be used
            return null;
        }
    }
}