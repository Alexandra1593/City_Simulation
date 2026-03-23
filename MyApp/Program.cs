using System;

namespace ProiectSPG
{
    internal class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            using (var app = new MyApp())
            {
                app.Initialize();
                app.Run();
            }
        }
    }
}
