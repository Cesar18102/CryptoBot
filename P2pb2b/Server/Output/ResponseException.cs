using System;

using Newtonsoft.Json;

namespace P2pb2b.Server.Output
{
    [JsonObject(MemberSerialization.OptOut)]
    public class ResponseException : Exception
    {
        [JsonRequired]
        public string message { get; set; }

        public ResponseException() { }

        public ResponseException(string message) => this.message = message;

        public override string ToString() => "{ \"message\" : \"" + message + "\" }";
    }
}
