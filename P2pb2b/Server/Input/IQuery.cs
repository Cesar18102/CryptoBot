using System.Collections.Generic;

namespace P2pb2b.Server.Input
{
    public enum QueryMethod
    {
        GET,
        POST,
        POST_URLENCODED,
        POST_MULTIPART,
        DELETE
    };

    public interface IQuery
    {
        QueryMethod Method { get; }
        string ServerMethod { get; }

        string QueryBody { get; }
        string ParametersString { get; }
        IDictionary<string, string> Parameters { get; }
        IList<MultipartDataItem> MultipartData { get; }

        IList<string> NeededHeaders { get; }
        IList<string> NeededCookies { get; }

        IDictionary<string, string> Headers { get; }
    }
}
