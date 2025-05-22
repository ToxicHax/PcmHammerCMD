using System.Threading.Tasks;

namespace CMDVersion
{
    public interface IPipeCallback
    {
        Task<string> ProcessPipeMessage(string message);
    }
}