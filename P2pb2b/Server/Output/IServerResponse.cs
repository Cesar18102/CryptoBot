using System.Net;
using System.Collections.Generic;

namespace P2pb2b.Server.Output
{
    public interface IServerResponse
    {
        string Data { get; }
        IDictionary<string, string> Headers { get; }
        CookieContainer CookieContainer { get; }
    }
}
