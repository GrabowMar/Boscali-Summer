using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;
using BoscaliSummer.Core.Storage;
using BoscaliSummer.Modules.Cinematography.Domain;

namespace BoscaliSummer.Modules.Cinematography
{
    internal sealed class ShotStore
    {
        internal const int FileLimit = 1024 * 1024;
        private readonly string root;
        internal ShotStore(string folder) { root = Path.GetFullPath(folder); }

        internal bool Save(ShotPlan plan, out string error)
        {
            if (!ShotPlan.Validate(plan, out error)) return false;
            return Write(Path.Combine(root, plan.id + ".json"), plan, out error);
        }

        internal bool Load(string id, out ShotPlan plan, out string error)
        {
            plan = null; error = null;
            if (!ShotPlan.SafeId(id)) { error = "Invalid shot ID"; return false; }
            try
            {
                string path = Path.Combine(root, id + ".json");
                if(new FileInfo(path).Length>FileLimit) { error="Shot file exceeds 1 MiB";return false; }
                if(!DecodeShot(File.ReadAllText(path),out ShotPlan loaded,out error)) return false;
                if (loaded.id != id) { error = "Shot ID does not match its file"; return false; }
                plan = loaded;
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is System.Runtime.Serialization.SerializationException || e is XmlException || e is ArgumentException)
            { error = "Cannot load shot: " + e.Message; return false; }
        }

        internal bool SaveSequence(ShotSequence sequence,out string error)
        { if(!ShotSequence.Validate(sequence,out error)) return false; return Write(Path.Combine(root,"Edits",sequence.id+".json"),sequence,out error); }
        internal bool LoadSequence(string id,out ShotSequence sequence,out string error)
        {
            sequence=null;error=null;
            if(!ShotPlan.SafeId(id)) { error="Invalid edit ID";return false; }
            try
            {
                string path=Path.Combine(root,"Edits",id+".json");
                if(new FileInfo(path).Length>FileLimit) { error="Edit file exceeds 1 MiB";return false; }
                if(!DecodeSequence(File.ReadAllText(path),out var loaded,out error)) return false;
                if(loaded.id!=id) { error="Edit ID does not match its file";return false; }
                sequence=loaded;return true;
            }
            catch(Exception e) when(e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            { error="Cannot load edit: "+e.Message;return false; }
        }
        internal string[] ListShots()
        {
            if(!Directory.Exists(root)) return Array.Empty<string>();
            var ids=new System.Collections.Generic.List<string>(64);
            int inspected=0;
            foreach(string file in Directory.EnumerateFiles(root,"*.json"))
            { string id=Path.GetFileNameWithoutExtension(file);if(ShotPlan.SafeId(id)) ids.Add(id);if(++inspected==64) break; }
            ids.Sort(StringComparer.Ordinal);return ids.ToArray();
        }

        internal static string Encode(object value)
        {
            using var stream=new MemoryStream();Serializer(value.GetType()).WriteObject(stream,value);
            if(stream.Length>FileLimit) throw new ArgumentException("Payload exceeds 1 MiB");
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        internal static bool DecodeShot(string json,out ShotPlan plan,out string error)
        { if(!Decode(json,out plan,out error)) return false;return ShotPlan.Validate(plan,out error); }
        internal static bool DecodeSequence(string json,out ShotSequence sequence,out string error)
        { if(!Decode(json,out sequence,out error)) return false;return ShotSequence.Validate(sequence,out error); }
        internal static bool DecodeOptions(string json,float duration,out ShotOptions options,out string error)
        { if(!Decode(json,out options,out error)) return false;return ShotOptions.Validate(options,duration,out error); }
        internal static bool DecodeCommand(string json,out System.Collections.Generic.Dictionary<string,object> command,out string error)
        {
            if(!Decode(json,out command,out error)) return false;
            if(command.Count>16) { error="Command exceeds 16 fields";return false; }
            return true;
        }
        private static bool Decode<T>(string json,out T value,out string error) where T:class
        {
            value=null;error=null;
            if(json==null || json.Length>FileLimit || Encoding.UTF8.GetByteCount(json)>FileLimit) { error="JSON payload missing or exceeds 1 MiB";return false; }
            try
            {
                using var stream=new MemoryStream(Encoding.UTF8.GetBytes(json));
                var quotas=new XmlDictionaryReaderQuotas { MaxDepth=16,MaxArrayLength=4096,MaxStringContentLength=FileLimit };
                using var reader=JsonReaderWriterFactory.CreateJsonReader(stream,quotas);
                value=(T)Serializer(typeof(T)).ReadObject(reader);
                if(value==null) { error="JSON object required";return false; }return true;
            }
            catch(Exception e) when(e is System.Runtime.Serialization.SerializationException || e is XmlException || e is ArgumentException)
            { error="Invalid JSON: "+e.Message;return false; }
        }

        internal bool SaveTake(TakeLog take, out string error)
        {
            error = null;
            if (take == null || !take.IsFinished || !ShotPlan.SafeId(take.id))
            { error = "Only finished takes with valid IDs can be saved"; return false; }
            return Write(Path.Combine(root, "Takes", take.id + ".json"), take, out error);
        }

        private static DataContractJsonSerializer Serializer(Type type) => new DataContractJsonSerializer(type,
            new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = 65536,UseSimpleDictionaryFormat=true,
                KnownTypes=new[] { typeof(System.Collections.Generic.Dictionary<string,object>),typeof(System.Collections.Generic.Dictionary<string,string>),
                    typeof(string[]),typeof(object[]),typeof(float[]) } });

        private static bool Write(string path, object value, out string error)
        {
            try
            {
                return AtomicFile.WriteAllText(path, Encode(value), out error);
            }
            catch (Exception e) when (e is System.Runtime.Serialization.SerializationException || e is ArgumentException)
            { error = "Cannot save: " + e.Message; return false; }
        }
    }
}
