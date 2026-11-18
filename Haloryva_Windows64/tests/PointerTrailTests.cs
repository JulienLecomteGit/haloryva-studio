using System;
using System.IO;
using System.Text;

namespace CursorFlow
{
    internal static class PointerTrailTests
    {
        private static int Main()
        {
            var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
            var error = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false));
            output.AutoFlush = error.AutoFlush = true;
            Console.SetOut(output); Console.SetError(error);
            return PointerTrailSelfTest.Run();
        }
    }
}
