using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace IslandShot
{
    // ---------------------------------------------------------------------
    // 模型配置 + Key 存储（阶段二 M1）
    //
    // build\data\models.txt   一行一个模型：id|label|baseUrl|model|keyRef|vision
    // build\data\keys.txt     一行一个 key：  keyRef|base64(DPAPI密文)
    //                                          keyRef|plain:明文   ← 手改调试用
    //
    // 不要引任何 JSON 库（csc 直编、没有 NuGet），所以用行格式 + base64。
    // ---------------------------------------------------------------------
    internal sealed class AiModelConfig
    {
        public string Id = "";
        public string Label = "";
        public string BaseUrl = "";
        public string Model = "";
        public string KeyRef = "";
        public bool Vision;

        public string DisplayName()
        {
            if (!string.IsNullOrEmpty(Label)) return Label;
            return Model;
        }
    }

    internal static class AiConfigStore
    {
        const string DefaultModels =
            "# id|label|baseUrl|model|keyRef|vision\r\n" +
            "zhipu|智谱 GLM-4.7-Flash（免费）|https://open.bigmodel.cn/api/paas/v4|glm-4.7-flash|zhipu|false\r\n" +
            "deepseek|DeepSeek Flash|https://api.deepseek.com|deepseek-flash|deepseek|true\r\n";

        internal static string DataDir
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data"); }
        }

        internal static string ModelsPath { get { return Path.Combine(DataDir, "models.txt"); } }
        internal static string KeysPath { get { return Path.Combine(DataDir, "keys.txt"); } }

        static void EnsureDir()
        {
            try { if (!Directory.Exists(DataDir)) Directory.CreateDirectory(DataDir); }
            catch (Exception) { }
        }

        // ------------------------------------------------------------- 模型

        internal static List<AiModelConfig> LoadModels()
        {
            var list = new List<AiModelConfig>();
            try
            {
                EnsureDir();
                if (!File.Exists(ModelsPath))
                {
                    File.WriteAllText(ModelsPath, DefaultModels, new UTF8Encoding(false));
                }
                foreach (string raw in File.ReadAllLines(ModelsPath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    string[] parts = line.Split('|');
                    if (parts.Length < 4) continue;

                    var model = new AiModelConfig();
                    model.Id = parts[0].Trim();
                    model.Label = parts[1].Trim();
                    model.BaseUrl = parts[2].Trim();
                    model.Model = parts[3].Trim();
                    model.KeyRef = parts.Length > 4 ? parts[4].Trim() : "";
                    model.Vision = parts.Length > 5 && parts[5].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                    if (model.Id.Length == 0 || model.Model.Length == 0) continue;
                    list.Add(model);
                }
            }
            catch (Exception) { }

            if (list.Count == 0)
            {
                // 文件被写坏/读不了时，至少留一条能用的，别让面板空着
                var fallback = new AiModelConfig();
                fallback.Id = "zhipu";
                fallback.Label = "智谱 GLM-4.7-Flash（免费）";
                fallback.BaseUrl = "https://open.bigmodel.cn/api/paas/v4";
                fallback.Model = "glm-4.7-flash";
                fallback.KeyRef = "zhipu";
                list.Add(fallback);
            }
            return list;
        }

        internal static void SaveModel(AiModelConfig model)
        {
            if (model == null || string.IsNullOrEmpty(model.Id)) return;
            List<AiModelConfig> list = LoadModels();
            int index = -1;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Id == model.Id) { index = i; break; }
            }
            if (index >= 0) list[index] = model;
            else list.Add(model);
            WriteModels(list);
        }

        /// <summary>删掉一条模型（keys.txt 里对应的行保留，删模型不连 key 一起删）。</summary>
        internal static void DeleteModel(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            List<AiModelConfig> list = LoadModels();
            var keep = new List<AiModelConfig>();
            foreach (AiModelConfig item in list)
            {
                if (item.Id != id) keep.Add(item);
            }
            if (keep.Count == 0) return;   // 至少留一条，不然 chip 没东西可选
            WriteModels(keep);
        }

        static void WriteModels(List<AiModelConfig> list)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# id|label|baseUrl|model|keyRef|vision");
            foreach (AiModelConfig item in list)
            {
                sb.Append(item.Id).Append('|')
                  .Append(item.Label).Append('|')
                  .Append(item.BaseUrl).Append('|')
                  .Append(item.Model).Append('|')
                  .Append(item.KeyRef).Append('|')
                  .Append(item.Vision ? "true" : "false")
                  .AppendLine();
            }
            try
            {
                EnsureDir();
                File.WriteAllText(ModelsPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        // ---------------------------------------------------------------- Key

        /// <summary>读 key；没有就返回 null。plain: 前缀表示明文直读。</summary>
        internal static string GetKey(string keyRef)
        {
            if (string.IsNullOrEmpty(keyRef)) return null;
            try
            {
                if (!File.Exists(KeysPath)) return null;
                foreach (string raw in File.ReadAllLines(KeysPath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int bar = line.IndexOf('|');
                    if (bar <= 0) continue;
                    if (!line.Substring(0, bar).Trim().Equals(keyRef, StringComparison.OrdinalIgnoreCase)) continue;

                    string value = line.Substring(bar + 1).Trim();
                    if (value.StartsWith("plain:", StringComparison.OrdinalIgnoreCase))
                    {
                        return value.Substring(6);
                    }
                    try
                    {
                        byte[] cipher = Convert.FromBase64String(value);
                        byte[] plain = ProtectedData.Unprotect(cipher, null, DataProtectionScope.CurrentUser);
                        return Encoding.UTF8.GetString(plain);
                    }
                    catch (Exception) { return null; }
                }
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>加密写 key。返回 false = 加密失败（调用方提示用户手改用 plain:）。</summary>
        internal static bool SaveKey(string keyRef, string key)
        {
            if (string.IsNullOrEmpty(keyRef)) return true;

            string stored;
            try
            {
                byte[] plain = Encoding.UTF8.GetBytes(key == null ? "" : key);
                byte[] cipher = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
                stored = Convert.ToBase64String(cipher);
            }
            catch (Exception) { return false; }

            try
            {
                EnsureDir();
                var lines = new List<string>();
                bool replaced = false;
                if (File.Exists(KeysPath))
                {
                    foreach (string raw in File.ReadAllLines(KeysPath, Encoding.UTF8))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0) continue;
                        int bar = line.IndexOf('|');
                        if (bar > 0 && line.Substring(0, bar).Trim().Equals(keyRef, StringComparison.OrdinalIgnoreCase))
                        {
                            lines.Add(keyRef + "|" + stored);
                            replaced = true;
                        }
                        else lines.Add(line);
                    }
                }
                if (!replaced) lines.Add(keyRef + "|" + stored);
                File.WriteAllText(KeysPath, string.Join(Environment.NewLine, lines.ToArray()) + Environment.NewLine,
                    new UTF8Encoding(false));
                return true;
            }
            catch (Exception) { return false; }
        }

        internal static bool HasKey(string keyRef)
        {
            string key = GetKey(keyRef);
            return !string.IsNullOrEmpty(key);
        }
    }
}
