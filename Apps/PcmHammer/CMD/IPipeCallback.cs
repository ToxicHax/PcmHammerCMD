using System.IO;
using System.Threading.Tasks;

namespace CMDVersion
{
    public interface IPipeCallback
    {
        /// <summary>
        /// Processes a message/command received
        /// </summary>
        /// <param name="server">the current server context processing this command</param>
        /// <param name="message">message/command received from client pipe</param>
        /// <returns></returns>
        Task<string> ProcessPipeMessage(StreamWriter server, string message);
    }
}