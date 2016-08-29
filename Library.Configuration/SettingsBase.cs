using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Library.Collections;
using Library.Io;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Library.Configuration
{
    public interface ISettingContent
    {
        string Name { get; }
        Type Type { get; }
        object Value { get; set; }
    }

    public class SettingContent<T> : ISettingContent
    {
        public SettingContent()
        {

        }

        public T Value { get; set; }

        #region ISettingContent

        public string Name { get; set; }

        public Type Type
        {
            get
            {
                return typeof(T);
            }
        }

        object ISettingContent.Value
        {
            get
            {
                return this.Value;
            }
            set
            {
                this.Value = (T)value;
            }
        }

        #endregion
    }

    public abstract class SettingsBase : ISettings
    {
        private Dictionary<string, Content> _dic = new Dictionary<string, Content>();
        private const int _cacheSize = 1024 * 1024;

        protected SettingsBase(IEnumerable<ISettingContent> contents)
        {
            foreach (var content in contents)
            {
                _dic[content.Name] = new Content()
                {
                    Type = content.Type,
                    Value = content.Value,
                };
            }
        }

        protected object this[string propertyName]
        {
            get
            {
                return _dic[propertyName].Value;
            }
            set
            {
                _dic[propertyName].Value = value;
            }
        }

        private static FileStream GetUniqueFileStream(string path)
        {
            if (!File.Exists(path))
            {
                try
                {
                    return new FileStream(path, FileMode.CreateNew);
                }
                catch (DirectoryNotFoundException)
                {
                    throw;
                }
                catch (IOException)
                {

                }
            }

            for (int index = 1; ; index++)
            {
                string text = string.Format(@"{0}/{1} ({2}){3}",
                    Path.GetDirectoryName(path),
                    Path.GetFileNameWithoutExtension(path),
                    index,
                    Path.GetExtension(path));

                if (!File.Exists(text))
                {
                    try
                    {
                        return new FileStream(text, FileMode.CreateNew);
                    }
                    catch (DirectoryNotFoundException)
                    {
                        throw;
                    }
                    catch (IOException)
                    {
                        if (index > 1024) throw;
                    }
                }
            }
        }

        #region ISettings

        public virtual void Load(string directoryPath)
        {
            var sw = new Stopwatch();
            sw.Start();

            if (!Directory.Exists(directoryPath)) Directory.CreateDirectory(directoryPath);

            // Tempファイルを削除する。
            {
                foreach (var path in Directory.GetFiles(directoryPath, "*", SearchOption.TopDirectoryOnly))
                {
                    var ext = Path.GetExtension(path);

                    if (ext == ".tmp")
                    {
                        try
                        {
                            File.Delete(path);
                        }
                        catch (Exception)
                        {

                        }
                    }
                }
            }

            var successNames = new LockedHashSet<string>();

            // Json
            foreach (var extension in new string[] { ".config.gz", ".config.gz.bak" })
            {
                Parallel.ForEach(Directory.GetFiles(directoryPath), new ParallelOptions() { MaxDegreeOfParallelism = 8 }, configPath =>
                {
                    if (!configPath.EndsWith(extension)) return;

                    var name = Path.GetFileName(configPath.Substring(0, configPath.Length - extension.Length));
                    if (successNames.Contains(name)) return;

                    Content content = null;
                    if (!_dic.TryGetValue(name, out content)) return;

                    try
                    {
                        using (FileStream stream = new FileStream(configPath, FileMode.Open))
                        using (CacheStream cacheStream = new CacheStream(stream, _cacheSize, BufferManager.Instance))
                        using (GZipStream decompressStream = new GZipStream(cacheStream, CompressionMode.Decompress))
                        {
                            using (var streamReader = new StreamReader(decompressStream, new UTF8Encoding(false)))
                            using (var jsonTextReader = new JsonTextReader(streamReader))
                            {
                                var serializer = new JsonSerializer();
                                serializer.MissingMemberHandling = MissingMemberHandling.Ignore;

                                serializer.TypeNameHandling = TypeNameHandling.None;
                                serializer.Converters.Add(new Newtonsoft.Json.Converters.IsoDateTimeConverter());
                                serializer.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
                                serializer.ContractResolver = new CustomContractResolver();

                                content.Value = serializer.Deserialize(jsonTextReader, content.Type);
                            }
                        }

                        successNames.Add(name);
                    }
                    catch (Exception e)
                    {
                        Log.Warning(e);
                    }
                });
            }

            // DataContractSerializer Text
            foreach (var extension in new string[] { ".gz", ".gz.bak" })
            {
                Parallel.ForEach(Directory.GetFiles(directoryPath), new ParallelOptions() { MaxDegreeOfParallelism = 8 }, configPath =>
                {
                    if (!configPath.EndsWith(extension)) return;

                    var name = Path.GetFileName(configPath.Substring(0, configPath.Length - extension.Length));
                    if (successNames.Contains(name)) return;

                    Content content = null;
                    if (!_dic.TryGetValue(name, out content)) return;

                    try
                    {
                        using (FileStream stream = new FileStream(configPath, FileMode.Open))
                        using (CacheStream cacheStream = new CacheStream(stream, _cacheSize, BufferManager.Instance))
                        using (GZipStream decompressStream = new GZipStream(cacheStream, CompressionMode.Decompress))
                        {
                            using (var xml = XmlReader.Create(decompressStream))
                            {
                                var deserializer = new DataContractSerializer(content.Type);
                                content.Value = deserializer.ReadObject(xml);
                            }
                        }

                        successNames.Add(name);
                    }
                    catch (Exception e)
                    {
                        Log.Warning(e);
                    }
                });
            }

            // DataContractSerializer Binary
            foreach (var extension in new string[] { ".v2", ".v2.bak" })
            {
                Parallel.ForEach(Directory.GetFiles(directoryPath), new ParallelOptions() { MaxDegreeOfParallelism = 8 }, configPath =>
                {
                    if (!configPath.EndsWith(extension)) return;

                    var name = Path.GetFileName(configPath.Substring(0, configPath.Length - extension.Length));
                    if (successNames.Contains(name)) return;

                    Content content = null;
                    if (!_dic.TryGetValue(name, out content)) return;

                    try
                    {
                        using (FileStream stream = new FileStream(configPath, FileMode.Open))
                        using (CacheStream cacheStream = new CacheStream(stream, _cacheSize, BufferManager.Instance))
                        using (GZipStream decompressStream = new GZipStream(cacheStream, CompressionMode.Decompress))
                        {
                            using (var xml = XmlDictionaryReader.CreateBinaryReader(decompressStream, XmlDictionaryReaderQuotas.Max))
                            {
                                var deserializer = new DataContractSerializer(content.Type);
                                content.Value = deserializer.ReadObject(xml);
                            }
                        }

                        successNames.Add(name);
                    }
                    catch (Exception e)
                    {
                        Log.Warning(e);
                    }
                });
            }

            sw.Stop();
            Debug.WriteLine("Settings Load {0} {1}", Path.GetFileName(directoryPath), sw.ElapsedMilliseconds);
        }

        public virtual void Save(string directoryPath)
        {
            var sw = new Stopwatch();
            sw.Start();

            if (!Directory.Exists(directoryPath)) Directory.CreateDirectory(directoryPath);

            Parallel.ForEach(_dic, new ParallelOptions() { MaxDegreeOfParallelism = 8 }, item =>
            {
                try
                {
                    var name = item.Key;
                    var type = item.Value.Type;
                    var value = item.Value.Value;

                    string uniquePath = null;

                    using (FileStream stream = SettingsBase.GetUniqueFileStream(Path.Combine(directoryPath, name + ".tmp")))
                    using (CacheStream cacheStream = new CacheStream(stream, _cacheSize, BufferManager.Instance))
                    using (GZipStream compressStream = new GZipStream(cacheStream, CompressionMode.Compress))
                    {
                        uniquePath = stream.Name;

                        using (var streamWriter = new StreamWriter(compressStream, new UTF8Encoding(false)))
                        using (var jsonTextWriter = new JsonTextWriter(streamWriter))
                        {
                            var serializer = new JsonSerializer();
                            serializer.Formatting = Newtonsoft.Json.Formatting.None;

                            serializer.TypeNameHandling = TypeNameHandling.None;
                            serializer.Converters.Add(new Newtonsoft.Json.Converters.IsoDateTimeConverter());
                            serializer.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
                            serializer.ContractResolver = new CustomContractResolver();

                            serializer.Serialize(jsonTextWriter, value);
                        }
                    }

                    string newPath = Path.Combine(directoryPath, name + ".config.gz");
                    string bakPath = Path.Combine(directoryPath, name + ".config.gz.bak");

                    if (File.Exists(newPath))
                    {
                        if (File.Exists(bakPath))
                        {
                            File.Delete(bakPath);
                        }

                        File.Move(newPath, bakPath);
                    }

                    File.Move(uniquePath, newPath);

                    {
                        foreach (var extension in new string[] { ".v2", ".v2.bak", ".gz", ".gz.bak" })
                        {
                            string deleteFilePath = Path.Combine(directoryPath, name + extension);

                            if (File.Exists(deleteFilePath))
                            {
                                File.Delete(deleteFilePath);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.Warning(e);
                }
            });

            sw.Stop();
            Debug.WriteLine("Settings Save {0} {1}", Path.GetFileName(directoryPath), sw.ElapsedMilliseconds);
        }

        class CustomContractResolver : DefaultContractResolver
        {
            protected override JsonContract CreateContract(Type objectType)
            {
                if (objectType.GetInterfaces().Any(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IDictionary<,>)))
                {
                    return base.CreateArrayContract(objectType);
                }

                if (System.Attribute.GetCustomAttributes(objectType).Any(n => n is DataContractAttribute))
                {
                    var objectContract = base.CreateObjectContract(objectType);
                    objectContract.DefaultCreatorNonPublic = false;
                    objectContract.DefaultCreator = () => FormatterServices.GetUninitializedObject(objectContract.CreatedType);

                    return objectContract;
                }

                return base.CreateContract(objectType);
            }
        }

        #endregion

        protected bool Contains(string propertyName)
        {
            return _dic.ContainsKey(propertyName);
        }

        private class Content
        {
            public Type Type { get; set; }
            public object Value { get; set; }
        }
    }
}
