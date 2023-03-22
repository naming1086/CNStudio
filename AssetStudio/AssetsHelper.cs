using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Globalization;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;

namespace AssetStudio
{
    public static class AssetsHelper
    {
        public const string CABMapName = "Maps";

        public static CancellationTokenSource tokenSource = new CancellationTokenSource();

        private static Dictionary<string, string> CABMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static AssetsManager assetsManager = new AssetsManager() { Silent = true, SkipProcess = true, ResolveDependencies = false };

        public static bool TryGet(string key, out string value) => CABMap.TryGetValue(key, out value);

        public static string[] GetMaps()
        {
            Directory.CreateDirectory(CABMapName);
            var files = Directory.GetFiles(CABMapName, "*.bin", SearchOption.TopDirectoryOnly);
            return files.Select(x => Path.GetFileNameWithoutExtension(x)).ToArray();
        }

        public class NMBundleFile
        {
            public string bundleName;
            public string fileName;
            public string cabName;
            public Dictionary<long, NMAssetObject> objDict;
            public List<string> depList;
        }

        public class NMAssetObject
        {
            public long pathID;
            public string name;
            public string classType;
            public long byteSize;
            public string objInfo;
        }
        public static string GetObjectName(AssetStudio.Object obj)
        {
            if (obj is Shader)
            {
                Shader s = obj as Shader;
                return s.m_ParsedForm.m_Name;
            }
            else
            {
                Type t = obj.GetType();
                TypeInfo tf = t.GetTypeInfo();
                var fd = tf.GetField("m_Name");
                if (fd != null)
                {
                    return fd.GetValue(obj).ToString();
                }
            }


            return "";
        }

        public static string GetObjectClassType(AssetStudio.Object obj)
        {
            if (obj is MonoBehaviour)
            {
                var asset = obj as MonoBehaviour;
                if (!asset.m_Script.IsNull && asset.assetsFile.ObjectsDic.ContainsKey(asset.m_Script.m_PathID))
                {
                    var scriptObj = asset.assetsFile.ObjectsDic[asset.m_Script.m_PathID];
                    if (scriptObj is MonoScript)
                    {
                        MonoScript mo = scriptObj as MonoScript;
                        return string.Format("{0} ({1}.{2})", obj.type.ToString(), mo.m_Namespace, mo.m_ClassName);
                    }
                }
            }
            else if (obj is MonoScript)
            {
                MonoScript mo = obj as MonoScript;
                return string.Format("{0} ({1}.{2})", obj.type.ToString(), mo.m_Namespace, mo.m_ClassName);
            }


            return obj.type.ToString();
        }

        public class NMBundleInfo
        {
            public string bundleName;
            public List<NMBundleFile> bundleFileList;
        }

        public static void BuildCABMap(string[] files, string mapName)
        {
            Logger.Info($"Processing...");
            try
            {
                CABMap.Clear();
                var collision = 0;
                Dictionary<string, NMBundleInfo> result = new Dictionary<string, NMBundleInfo>();
                assetsManager.SkipProcess = false;
                    assetsManager.LoadFiles(files);

                    if (assetsManager.assetsFileList.Count > 0)
                    {
                        foreach (var assetsFile in assetsManager.assetsFileList)
                        {

                            NMBundleFile bundleFile = new NMBundleFile();
                            bundleFile.fileName = assetsFile.originalPath;
                            bundleFile.objDict = new Dictionary<long, NMAssetObject>();
                            bundleFile.depList = new List<string>();
                            bundleFile.bundleName = assetsFile.fileName;
                            bundleFile.cabName = assetsFile.fileName;

                            if (assetsFile.Objects.Count>0)
                            {
                                int n = 0;
                                foreach (AssetStudio.Object asset in assetsFile.Objects)
                                {
                                    switch (asset)
                                    {
                                        case AssetBundle bundle: //这个是添加在deps部分
                                        {
                                                bundleFile.bundleName = bundle.m_AssetBundleName;
                                                bundleFile.depList.AddRange(bundle.m_Dependencies);
                                            }
                                            break;
                                        default: //这个是添加在objs部分
                                            {
                                                if (asset is Transform || asset is AssetBundle)
                                                {
                                                    continue;
                                                }
                                                if (asset is GameObject)
                                                {
                                                    GameObject obj = asset as GameObject;
                                                    if (!obj.m_Transform.m_Father.IsNull)
                                                    {
                                                        continue;
                                                    }
                                                }
                                                if (!bundleFile.objDict.ContainsKey(asset.m_PathID))
                                                {
                                                    NMAssetObject assetObj = new NMAssetObject();
                                                    assetObj.pathID = asset.m_PathID;
                                                    assetObj.name = GetObjectName(asset);
                                                    assetObj.classType = GetObjectClassType(asset);
                                                    assetObj.byteSize = asset.byteSize;
                                                    assetObj.objInfo = null;
                                                    if (asset is Texture2D)
                                                    {
                                                        Texture2D texture = asset as Texture2D;
                                                        assetObj.objInfo = $"width: {texture.m_Width} height: {texture.m_Height} format: {texture.m_TextureFormat}";
                                                    }
                                                    else if (asset is Mesh)
                                                    {
                                                        Mesh mesh = asset as Mesh;
                                                        assetObj.objInfo = $"vertices num: {mesh.m_VertexCount} triangle num: {mesh.m_Indices.Count / 3}";
                                                    }
                                                    else
                                                    {
                                                        assetObj.objInfo = asset.GetType().Name;
                                                    }
                                                    bundleFile.objDict.Add(asset.m_PathID, assetObj);
                                                }
                                            }
                                            break;
                                    }
                                }


                                if (bundleFile.bundleName != null)
                                {
                                    NMBundleInfo info = null;
                                    if (!result.TryGetValue(bundleFile.bundleName, out info))
                                    {
                                        info = new NMBundleInfo();
                                        info.bundleName = bundleFile.bundleName;
                                        info.bundleFileList = new List<NMBundleFile>();
                                        result.Add(info.bundleName, info);
                                    }
                                    info.bundleFileList.Add(bundleFile);
                                }
                            }
                        }

                        // log
                        StringBuilder str = new StringBuilder();
                        int num = 0;
                        foreach (var bundleInfo in result)
                        {
                            str.AppendFormat("{0}. {1}", ++num, bundleInfo.Key);
                            str.AppendLine();

                            foreach (var bundleFile in bundleInfo.Value.bundleFileList)
                            {
                                str.AppendFormat("{0}", bundleFile.fileName);
                                str.AppendLine();
                                str.AppendFormat("{0}", bundleFile.cabName);
                                str.AppendLine();
                                if (bundleFile.objDict.Count> 0)
                                {
                                    str.AppendLine("  objs:");
                                    List<NMAssetObject> objList = new List<NMAssetObject>();
                                    foreach (var item in bundleFile.objDict)
                                    {
                                        objList.Add(item.Value);
                                    }
                                    objList.Sort((a, b) =>
                                    {
                                        return a.classType.CompareTo(b.classType);
                                    });

                                    foreach (var item in objList)
                                    {
                                        str.AppendFormat("    {0} : {1} : {2} : {3} : {4}\n", item.pathID, item.name, item.classType, item.byteSize, !string.IsNullOrEmpty(item.objInfo) ? item.objInfo : "");
                                    }

                                    if (bundleFile.depList.Count > 0)
                                    {
                                        str.AppendLine("  deps:");
                                        foreach (var item in bundleFile.depList)
                                        {
                                            str.AppendFormat("    {0}", item);
                                            str.AppendLine();
                                        }
                                    }
                                }

                            }
                            str.AppendLine();
                        }
                        File.WriteAllText(@"G:\zhushenzhizhan\123.log", str.ToString());
                        // json log
                        string jsonStr = JsonConvert.SerializeObject(result, Formatting.Indented);
                        File.WriteAllText(@"G:\zhushenzhizhan\123.json", jsonStr);

                }
                Logger.Info("Done");

            }
            catch (Exception e)
            {
                Logger.Warning($"CABMap was not build, {e}");
            }
        }

        public static void LoadMap(string mapName)
        {
            Logger.Info($"Loading {mapName}");
            try
            {
                CABMap.Clear();
                using (var fs = File.OpenRead(Path.Combine(CABMapName, $"{mapName}.bin")))
                using (var reader = new BinaryReader(fs))
                {
                    var count = reader.ReadInt32();
                    for (int i = 0; i < count; i++)
                    {
                        var cab = reader.ReadString();
                        var path = reader.ReadString();
                        CABMap.Add(cab, path);
                    }
                }
                Logger.Info($"Loaded {mapName} !!");
            }
            catch (Exception e)
            {
                Logger.Warning($"{mapName} was not loaded, {e}");
            }
        }

        public static AssetEntry[] BuildAssetMap(string[] files, ClassIDType[] typeFilters = null, Regex[] nameFilters = null, Regex[] containerFilters = null)
        {
            var assets = new List<AssetEntry>();
            for (int i = 0; i < files.Length; i++)
            {
                var file = files[i];
                assetsManager.LoadFiles(file);
                if (assetsManager.assetsFileList.Count > 0)
                {
                    var containers = new List<(PPtr<Object>, string)>();
                    var mihoyoBinDataNames = new List<(PPtr<Object>, string)>();
                    var objectAssetItemDic = new Dictionary<Object, AssetEntry>();
                    var animators = new List<(PPtr<Object>, AssetEntry)>();
                    foreach (var assetsFile in assetsManager.assetsFileList)
                    {
                        assetsFile.m_Objects = ObjectInfo.Filter(assetsFile.m_Objects);

                        foreach (var objInfo in assetsFile.m_Objects)
                        {
                            var objectReader = new ObjectReader(assetsFile.reader, assetsFile, objInfo);
                            var obj = new Object(objectReader);
                            var asset = new AssetEntry()
                            {
                                Source = file,
                                PathID = objectReader.m_PathID,
                                Type = objectReader.type,
                                Container = ""
                            };
                        
                            var exportable = true;
                            switch (objectReader.type)
                            {
                                case ClassIDType.AssetBundle:
                                    var assetBundle = new AssetBundle(objectReader);
                                    foreach (var m_Container in assetBundle.m_Container)
                                    {
                                        var preloadIndex = m_Container.Value.preloadIndex;
                                        var preloadSize = m_Container.Value.preloadSize;
                                        var preloadEnd = preloadIndex + preloadSize;
                                        for (int k = preloadIndex; k < preloadEnd; k++)
                                        {
                                            containers.Add((assetBundle.m_PreloadTable[k], m_Container.Key));
                                        }
                                    }
                                    obj = null;
                                    asset.Name = assetBundle.m_Name;
                                    exportable = false;
                                    break;
                                case ClassIDType.GameObject:
                                    var gameObject = new GameObject(objectReader);
                                    obj = gameObject;
                                    asset.Name = gameObject.m_Name;
                                    exportable = false;
                                    break;
                                case ClassIDType.Shader:
                                    asset.Name = objectReader.ReadAlignedString();
                                    if (string.IsNullOrEmpty(asset.Name))
                                    {
                                        var m_parsedForm = new SerializedShader(objectReader);
                                        asset.Name = m_parsedForm.m_Name;
                                    }
                                    break;
                                case ClassIDType.Animator:
                                    var component = new PPtr<Object>(objectReader);
                                    animators.Add((component, asset));
                                    break;
                                default:
                                    asset.Name = objectReader.ReadAlignedString();
                                    break;
                            }
                            if (obj != null)
                            {
                                objectAssetItemDic.Add(obj, asset);
                                assetsFile.AddObject(obj);
                            }
                            var isMatchRegex = nameFilters.IsNullOrEmpty() || nameFilters.Any(x => x.IsMatch(asset.Name) || asset.Type == ClassIDType.Animator);
                            var isFilteredType = typeFilters.IsNullOrEmpty() || typeFilters.Contains(asset.Type) || asset.Type == ClassIDType.Animator;
                            if (isMatchRegex && isFilteredType && exportable)
                            {
                                assets.Add(asset);
                            }
                        }
                    }
                    foreach ((var pptr, var asset) in animators)
                    {
                        if (pptr.TryGet<GameObject>(out var gameObject) && (nameFilters.IsNullOrEmpty() || nameFilters.Any(x => x.IsMatch(gameObject.m_Name))) && (typeFilters.IsNullOrEmpty() || typeFilters.Contains(asset.Type)))
                        {
                            asset.Name = gameObject.m_Name;
                        }
                    }
                    foreach ((var pptr, var container) in containers)
                    {
                        if (pptr.TryGet(out var obj))
                        {
                            var item = objectAssetItemDic[obj];
                            if (containerFilters.IsNullOrEmpty() || containerFilters.Any(x => x.IsMatch(container)))
                            {
                                item.Container = container;
                            }
                            else
                            {
                                assets.Remove(item);
                            }
                        }
                    }
                    Logger.Info($"Processed {Path.GetFileName(file)}");
                }
                assetsManager.Clear();
            }
            return assets.ToArray();
        }

        public static void ExportAssetsMap(AssetEntry[] toExportAssets, string name, string savePath)
        {
            ThreadPool.QueueUserWorkItem(state =>
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");

                Progress.Reset();

                string filename = Path.Combine(savePath, $"{name}.xml");
                var doc = new XDocument(
                    new XElement("Assets",
                        new XAttribute("filename", filename),
                        new XAttribute("createdAt", DateTime.UtcNow.ToString("s")),
                        toExportAssets.Select(
                            asset => new XElement("Asset",
                                new XElement("Name", asset.Name),
                                new XElement("Container", asset.Container),
                                new XElement("Type", new XAttribute("id", (int)asset.Type), asset.Type.ToString()),
                                new XElement("PathID", asset.PathID),
                                new XElement("Source", asset.Source)
                            )
                        )
                    )
                );
                doc.Save(filename);

                Logger.Info($"Finished exporting asset list with {toExportAssets.Length} items.");
                Logger.Info($"AssetMap build successfully !!");
            });
        }
    }
}
