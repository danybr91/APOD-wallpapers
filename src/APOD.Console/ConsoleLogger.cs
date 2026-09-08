using System;
using APOD.Core;

namespace APOD.Console
{
    public class ConsoleLogger : ILog
    {
        public void Info(string message)
        {
            System.Console.Error.WriteLine($"INFO\t=> {message}");
        }

        public void Error(string message)
        {
            System.Console.Error.WriteLine($"ERROR\t=> {message}");
        }

        public void Line(string message)
        {
            System.Console.WriteLine(message);
        }
    }
}
