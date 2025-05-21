using System;

namespace CMDVersion
{
    static class CMD_Program
    {
        /// <summary>
        /// The main entry point for the cmd application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            new CMD_MainNoForm();
        }
    }
}
