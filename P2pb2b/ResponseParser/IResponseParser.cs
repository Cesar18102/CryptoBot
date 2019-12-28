using System;
using System.Collections.Generic;

using P2pb2b.Server.Output;

namespace P2pb2b.ResponseParser
{
    public interface IResponseParser
    {
        dynamic Parse<E>(IServerResponse modelElementJSON) where E : Exception;
        IEnumerable<dynamic> ParseCollection<E>(IServerResponse modelElementJSON) where E : Exception;
    }
}
