// Fetches the script assistant's model weights (LocalScriptModel) into StreamingAssets, which git ignores because the file is
// about 1.1 GB. Downloads to a .part file and only renames it after the SHA-256 matches, so a cancelled or corrupt download
// never looks installed.
using System;
using System.IO;
using System.Security.Cryptography;
using FoodFactoryGame.Session.Employees;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace FoodFactoryGame.Editor
{
    public static class ScriptAssistantModelDownload
    {
        [MenuItem("FoodFactory/Download Script Assistant Model")]
        private static void Download()
        {
            var target = LocalScriptModel.Default.Path;
            if (File.Exists(target) && !EditorUtility.DisplayDialog("Script assistant model", "The model is already installed. Download it again?", "Download", "Cancel"))
                return;
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            var part = target + ".part";
            using (var request = new UnityWebRequest(LocalScriptModel.Default.Url, UnityWebRequest.kHttpVerbGET))
            {
                request.downloadHandler = new DownloadHandlerFile(part) { removeFileOnAbort = true };
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Script assistant model", $"Downloading {Path.GetFileName(target)} ({request.downloadProgress:P0})", request.downloadProgress))
                        request.Abort();
                    System.Threading.Thread.Sleep(100);
                }
                EditorUtility.ClearProgressBar();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"Script assistant model download failed: {request.error}");
                    return;
                }
            }
            string hash;
            using (var stream = File.OpenRead(part))
            using (var sha = SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            if (hash != LocalScriptModel.Default.Sha256)
            {
                File.Delete(part);
                Debug.LogError($"Script assistant model download is corrupt (SHA-256 {hash}); deleted it.");
                return;
            }
            if (File.Exists(target)) File.Delete(target);
            File.Move(part, target);
            AssetDatabase.Refresh();
            Debug.Log($"Script assistant model installed at {target}.");
        }
    }
}
