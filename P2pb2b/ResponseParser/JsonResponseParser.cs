using System;
using System.Collections.Generic;

using Newtonsoft.Json;

using P2pb2b.Server.Output;

namespace P2pb2b.ResponseParser
{
    public class JsonResponseParser : IResponseParser
    {
        public dynamic Parse<E>(IServerResponse modelElementJSON) where E : Exception
        {
            E ex = null;

            try { ex = JsonConvert.DeserializeObject<E>(modelElementJSON.Data); }
            catch { return JsonConvert.DeserializeObject(modelElementJSON.Data); }

            throw ex;
        }

        public IEnumerable<dynamic> ParseCollection<E>(IServerResponse modelElementJSON) where E : Exception
        {
            E ex = null;

            try { ex = JsonConvert.DeserializeObject<E>(modelElementJSON.Data); }
            catch { return JsonConvert.DeserializeObject<List<dynamic>>(modelElementJSON.Data); }

            throw ex;
        }
    }
}
