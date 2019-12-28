using System.Net;
using System.Threading.Tasks;

using P2pb2b.Server.Input;
using P2pb2b.Server.Output;

namespace P2pb2b.Server.Access
{
    public interface IServerCommunicator
    {
        string ServerURL { get; set; }
        Task<IServerResponse> SendQuery(IQuery query, CookieContainer container = null);
    }
}
