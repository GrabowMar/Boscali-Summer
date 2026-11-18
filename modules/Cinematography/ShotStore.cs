using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;
using BoscaliSummer.Modules.Cinematography.Domain;

namespace BoscaliSummer.Modules.Cinematography
{
    /// <summary>The JSON form of an edit. Files live with the nomodkit scenario that plays them, not in the game.</summary>
    internal static class ShotStore
    {
        internal const int Limit = 1024 * 1024;

        internal static string Encode(ShotSequence value)
        {
            using var stream=new MemoryStream();Serializer.WriteObject(stream,value);
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        internal static bool DecodeSequence(string json,out ShotSequence sequence,out string error)
        {
            sequence=null;error=null;
            if(json==null || json.Length>Limit) { error="JSON payload missing or exceeds 1 MiB";return false; }
            try
            {
                using var stream=new MemoryStream(Encoding.UTF8.GetBytes(json));
                var quotas=new XmlDictionaryReaderQuotas { MaxDepth=16,MaxArrayLength=4096,MaxStringContentLength=Limit };
                using var reader=JsonReaderWriterFactory.CreateJsonReader(stream,quotas);
                sequence=(ShotSequence)Serializer.ReadObject(reader);
            }
            catch(Exception e) when(e is System.Runtime.Serialization.SerializationException || e is XmlException || e is ArgumentException)
            { error="Invalid JSON: "+e.Message;return false; }
            return ShotSequence.Validate(sequence,out error);
        }
        private static DataContractJsonSerializer Serializer => new DataContractJsonSerializer(typeof(ShotSequence));
    }
}
