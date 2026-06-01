using System.Net;
using System.Threading.Tasks;

namespace Lilium.RemoteControl
{
    public interface IRequestHandler
    {
        Task HandleRequest(HttpListenerContext context);
        bool CanHandle(HttpListenerRequest request);
        void Cleanup();
    }
}