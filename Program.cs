using System;

namespace ReadKey
{
    internal class Program
    {

        static void Main()
        {
            while (true)
            {
                ConsoleKeyInfo cki = Console.ReadKey(true);
                Console.WriteLine($"Key: {cki.Key} Modifiers: {cki.Modifiers} KeyChar: '{(int)cki.KeyChar}' '{Print(cki.KeyChar)}'");
            }

            static string Print(char keyChar)
                => keyChar switch
                {
                    '\b' => "Backspace",
                    '\r' => "CR",
                    '\n' => "NL",
                    '\t' => "Tab",
                    (char)27 => "Escape",
                    _ => keyChar.ToString()
                };
        }
    }
}
