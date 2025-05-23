using System;
using System.Threading.Tasks;

namespace CMDVersion
{
    public class CMD_PipeCommand
    {
        public string command;
        public string description;
        public Func<string, Task<string>> funcMethod;

        public CMD_PipeCommand(string command, string description, Func<string, Task<string>> funcMethod)
        {
            this.command = command;
            this.description = description;
            this.funcMethod = funcMethod;
        }
    }
}