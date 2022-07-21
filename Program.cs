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

        static void Main(string[] args)
        {
            bool recordFunctional = Parse(args, 'f');
            bool recordHome = Parse(args, 'h');
            bool recordArrows = Parse(args, 'a');
            bool recordInsert = Parse(args, 'i');
            bool recordDelete = Parse(args, 'd');
            bool recordNumeric = Parse(args, 'n');

            byte verase = ConfigureTerminal();

            (string term, string actualPath, byte[] db) = ReadTerminalSettings();

            string? appMode = new ConsolePal.TerminalFormatStrings(new TermInfo.Database(term, db)).KeypadXmit;
            if (!string.IsNullOrEmpty(appMode))
            {
                Write(appMode); // transition into application mode
            }

            (string text, ConsoleKeyInfo keyInfo)[] functionalKeys =
                Enumerable.Range(1, 12).Select(i => ($"F{i}", CKI(default, ConsoleKey.F1 + i - 1))).ToArray();
            (string text, ConsoleKeyInfo keyInfo)[] functionalKeysCtrl =
                Enumerable.Range(1, 12).Select(i => ($"Ctrl+F{i}", CKI(default, ConsoleKey.F1 + i - 1, ConsoleModifiers.Control))).ToArray();
            (string text, ConsoleKeyInfo keyInfo)[] functionalKeysAlt =
                Enumerable.Range(1, 12).Select(i => ($"Alt+F{i}", CKI(default, ConsoleKey.F1 + i - 1, ConsoleModifiers.Alt))).ToArray();
            (string text, ConsoleKeyInfo keyInfo)[] functionalKeysShift =
                Enumerable.Range(1, 12).Select(i => ($"Shift+F{i}", CKI(default, ConsoleKey.F1 + i - 1, ConsoleModifiers.Shift))).ToArray();
            (string text, ConsoleKeyInfo keyInfo)[] functionalKeysAll =
                Enumerable.Range(1, 12).Select(i => ($"Ctrl+Alt+Shift+F{i}", CKI(default, ConsoleKey.F1 + i - 1, ConsoleModifiers.Shift | ConsoleModifiers.Control | ConsoleModifiers.Alt))).ToArray();
            // not testing Ctrl+Alt+Fx as on Ubuntu that I am using it's switching between GUI and Linux Console
            (string text, ConsoleKeyInfo keyInfo)[] homeEndUpDown = 
                new ConsoleKey[] { ConsoleKey.Home, ConsoleKey.End, ConsoleKey.PageUp, ConsoleKey.PageDown }
                .SelectMany(key => new (string text, ConsoleKeyInfo keyInfo)[]
                {
                    ($"{key}", CKI(default, key)),
                    ($"Ctrl+{key}", CKI(default, key, ConsoleModifiers.Control)),
                    ($"Alt+{key}", CKI(default, key, ConsoleModifiers.Alt)),
                    ($"Ctrl+Alt+{key}", CKI(default, key, ConsoleModifiers.Control | ConsoleModifiers.Alt)),
                    // not testing Shift as on Ubuntu it just scrolls the terminal for these keys
                }).ToArray();
            (string text, ConsoleKeyInfo keyInfo)[] arrows = 
                new ConsoleKey[] { ConsoleKey.LeftArrow, ConsoleKey.UpArrow, ConsoleKey.DownArrow, ConsoleKey.RightArrow }
                    .SelectMany(key => new (string text, ConsoleKeyInfo keyInfo)[]
                    {
                        ($"{key}", CKI(default, key)),
                        ($"Ctrl+{key}", CKI(default, key, ConsoleModifiers.Control)),
                        ($"Alt+{key}", CKI(default, key, ConsoleModifiers.Alt)),
                        ($"Shift+{key}", CKI(default, key, ConsoleModifiers.Shift)),
                        ($"Shift+Alt+{key}", CKI(default, key, ConsoleModifiers.Shift | ConsoleModifiers.Alt)),
                        // not testing Ctrl+Alt as on Ubuntu it does nothing
                    }).ToArray();
            (string text, ConsoleKeyInfo keyInfo)[] inserts = new[]
            {
                ("Insert", CKI(default, ConsoleKey.Insert)),
                ("Alt+Insert", CKI(default, ConsoleKey.Insert, ConsoleModifiers.Alt)),
            };
            (string text, ConsoleKeyInfo keyInfo)[] deletes = new[]
            {
                ("Delete", CKI(default, ConsoleKey.Delete)),
                ("Ctrl+Delete", CKI(default, ConsoleKey.Delete, ConsoleModifiers.Control)),
                ("Alt+Delete", CKI(default, ConsoleKey.Delete, ConsoleModifiers.Alt)),
                ("Shift+Delete", CKI(default, ConsoleKey.Delete, ConsoleModifiers.Shift)),
                ("Ctrl+Shift+Delete", CKI(default, ConsoleKey.Delete, ConsoleModifiers.Control | ConsoleModifiers.Shift)),
                ("Alt+Shift+Delete", CKI(default, ConsoleKey.Delete, ConsoleModifiers.Alt | ConsoleModifiers.Shift)),
                ("Ctrl+Alt+Shift+Delete", CKI(default, ConsoleKey.Delete, ConsoleModifiers.Control | ConsoleModifiers.Alt | ConsoleModifiers.Shift)),
            };
            (string text, ConsoleKeyInfo keyInfo)[] numericKeypadTestCases =
            {
                ("0 (using Numeric Keypad)", CKI('0', ConsoleKey.D0)),
                ("1 (using Numeric Keypad)", CKI('1', ConsoleKey.D1)),
                ("2 (using Numeric Keypad)", CKI('2', ConsoleKey.D2)),
                ("3 (using Numeric Keypad)", CKI('3', ConsoleKey.D3)),
                ("4 (using Numeric Keypad)", CKI('4', ConsoleKey.D4)),
                ("5 (using Numeric Keypad)", CKI('5', ConsoleKey.D5)),
                ("6 (using Numeric Keypad)", CKI('6', ConsoleKey.D6)),
                ("7 (using Numeric Keypad)", CKI('7', ConsoleKey.D7)),
                ("8 (using Numeric Keypad)", CKI('8', ConsoleKey.D8)),
                ("9 (using Numeric Keypad)", CKI('9', ConsoleKey.D9)),
                ("/ (divide sign using Numeric Keypad))", CKI('/', ConsoleKey.Divide)),
                ("* (multiply sign using Numeric Keypad))", CKI('*', ConsoleKey.Multiply)),
                ("- (minus sign using Numeric Keypad))", CKI('-', ConsoleKey.Subtract)),
                ("+ (plus sign using Numeric Keypad))", CKI('+', ConsoleKey.Add)),
                ("Enter (using Numeric Keypad))", CKI('\r', ConsoleKey.Enter)),
            };
            
            (string text, ConsoleKeyInfo keyInfo)[] numericKeypadTestCases2 =
            {
                ("Insert", CKI(default, ConsoleKey.Insert)),
                ("Delete", CKI(default, ConsoleKey.Delete)),
                ("End", CKI(default, ConsoleKey.End)),
                ("Down Arrow", CKI(default, ConsoleKey.DownArrow)),
                ("Page Down", CKI(default, ConsoleKey.PageDown)),
                ("Left Arrow", CKI(default, ConsoleKey.LeftArrow)),
                ("Begin (5)", CKI(default, ConsoleKey.NoName)),
                ("Right Arrow", CKI(default, ConsoleKey.RightArrow)),
                ("Home", CKI(default, ConsoleKey.Home)),
                ("Up Arrow", CKI(default, ConsoleKey.UpArrow)),
                ("Page Up", CKI(default, ConsoleKey.PageUp)),
                
                ("/ (divide sign using Numeric Keypad))", CKI('/', ConsoleKey.Divide)),
                ("* (multiply sign using Numeric Keypad))", CKI('*', ConsoleKey.Multiply)),
                ("- (minus sign using Numeric Keypad))", CKI('-', ConsoleKey.Subtract)),
                ("+ (plus sign using Numeric Keypad))", CKI('+', ConsoleKey.Add)),
                ("Enter (using Numeric Keypad))", CKI('\r', ConsoleKey.Enter)),
            };
            
            List<(ConsoleKeyInfo keyInfo, byte[] input, string comment)> recorded = new();

            try
            {
                if (recordArrows || recordDelete || recordFunctional || recordHome || recordInsert)
                {
                    do
                    {
                        WriteLine("1. Please don't use Numeric Keypad for now.");
                        WriteLine(
                            "2. If given key combination does not work (it's used by the OS or the terminal) press Spacebar.");
                        WriteLine("Press `y' or 'Y` if you understand both rules stated above.");
                    } while (!ReadAnswer('Y'));

                    RecordTestCases(recordFunctional, functionalKeys, recorded);
                    RecordTestCases(recordFunctional, functionalKeysCtrl, recorded);
                    RecordTestCases(recordFunctional, functionalKeysAlt, recorded);
                    RecordTestCases(recordFunctional, functionalKeysShift, recorded);
                    RecordTestCases(recordFunctional, functionalKeysAll, recorded);
                    RecordTestCases(recordHome, homeEndUpDown, recorded);
                    RecordTestCases(recordArrows, arrows, recorded);
                    RecordTestCases(recordInsert, inserts, recorded);
                    RecordTestCases(recordDelete, deletes, recorded);
                }

                if (recordNumeric)
                {
                    WriteLine(">>>>>>>>>>>> <<<<<<<<<<<<<<<<");
                    do
                    {
                        WriteLine("Please press `1' (one) using your Numeric Keypad. You might need to press Num Lock first to toggle the mode.");
                    } while (!ReadAnswer('1'));
                    RecordTestCases(recordNumeric, numericKeypadTestCases, recorded);

                    WriteLine(">>>>>>>>>>>> <<<<<<<<<<<<<<<<");
                    WriteLine("Please press Num Lock to toggle the mode.");
                    RecordTestCases(recordNumeric, numericKeypadTestCases2, recorded);
                }
            }
            finally
            {
                RestoreSettings();
            }

            PrintData(term, actualPath, db, verase, recorded);

            static ConsoleKeyInfo CKI(char ch, ConsoleKey key, ConsoleModifiers modifiers = 0)
                => new ConsoleKeyInfo(ch, key, (modifiers & ConsoleModifiers.Shift) != 0, (modifiers & ConsoleModifiers.Alt) != 0, (modifiers & ConsoleModifiers.Control) != 0);
        }

        private static bool Parse(string[] args, char letter)
            => args.Length == 0 || args[0].Contains(letter, StringComparison.OrdinalIgnoreCase);
        
        private static void RecordTestCases(bool record, (string text, ConsoleKeyInfo keyInfo)[] testCases, List<(ConsoleKeyInfo keyInfo, byte[] input, string comment)> recorded)
        {
            if (!record) return;

            byte* inputBuffer = stackalloc byte[1024];

            foreach ((string text, ConsoleKeyInfo keyInfo) in testCases)
            {
                WriteLine($"\nPlease press {text}");
                int bytesRead = read(STDIN_FILENO, inputBuffer, 1024);

                if (bytesRead > 1 || inputBuffer[0] != 27)
                {
                    recorded.Add((keyInfo, new ReadOnlySpan<byte>(inputBuffer, bytesRead).ToArray(), text));
                }
            }
        }

        private static void WriteLine(string text) => Write(text + "\n");

        private static void Write(string text)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(text);
            fixed (byte* pinned = utf8)
            {
                write(1, pinned, utf8.Length);
            }
        }

        private static bool ReadAnswer(char confirm)
        {
            byte* inputBuffer = stackalloc byte[1];
            read(STDIN_FILENO, inputBuffer, 1);
            return char.ToUpper((char)inputBuffer[0]) == confirm;
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
        
        private static void PrintData(string term, string actualPath, byte[] db, byte verase, List<(ConsoleKeyInfo keyInfo, byte[] input, string comment)> recorded)
        {
            StringBuilder sb = new(1000);
            sb.AppendLine("```cs");
            sb.Append(
$@"
public class {term.Replace('-', '_')}_Data : TerminalData
{{
    protected override string EncodingCharset => ""{EncodingHelper.GetCharset()}"";
    protected override string Term => ""{term}"";
    internal override byte Verase => {verase};
    protected override string EncodedTerminalDb => ""{Convert.ToBase64String(db)}""; // {actualPath}

    internal override IEnumerable<(byte[], ConsoleKeyInfo)> RecordedScenarios
    {{
        get
        {{
{string.Join(Environment.NewLine, recorded.Select(x => Format(x.keyInfo, x.input, x.comment)))}
        }}
    }}
}}
");
            sb.AppendLine("```");

            File.WriteAllText("upload_me.md", sb.ToString());
            WriteLine("Please upload the contents of upload_me.md. Thank you!");

            static string Format(ConsoleKeyInfo ki, byte[] input, string comment)
                => $"yield return (new byte[] {{ {string.Join(", ", input)} }}, new ConsoleKeyInfo({ToSource(ki.KeyChar)}, ConsoleKey.{ki.Key.ToString()}, {((ki.Modifiers & ConsoleModifiers.Shift) != 0).ToString().ToLower()}, {((ki.Modifiers & ConsoleModifiers.Alt) != 0).ToString().ToLower()}, {((ki.Modifiers & ConsoleModifiers.Control) != 0).ToString().ToLower()})); // {comment}";

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