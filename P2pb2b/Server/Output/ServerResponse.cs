using System.Net;
using System.Collections.Generic;

namespace P2pb2b.Server.Output
{
    public class ServerResponse : IServerResponse
    {
        public string Data { get; private set; }
        public IDictionary<string, string> Headers { get; private set; }
        public CookieContainer CookieContainer { get; private set; }

        public ServerResponse(string data, IDictionary<string, string> headers, CookieContainer cookieContainer)
        {
            Data = data;
            Headers = headers;
            CookieContainer = cookieContainer;
        }
    }
}
