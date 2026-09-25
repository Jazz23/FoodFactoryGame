// The script assistant's language model, run in-process by LLMUnity's llama.cpp backend on this client, so no server or
// network is involved. The weights are not in git: they live under StreamingAssets/Models and are fetched by the Editor menu
// FoodFactory > Download Script Assistant Model. Loading takes seconds and several GB of memory, so the model starts on
// first use (Warm) and stays loaded while this object lives. Only Apache-2.0 models are listed, so the game may ship them.
using System.IO;
using System.Threading.Tasks;
using LLMUnity;
using UnityEngine;

namespace FoodFactoryGame.Session.Employees
{
    // A GGUF model file under StreamingAssets, where to download it and its SHA-256.
    public sealed class ScriptModelFile
    {
        public readonly string File;
        public readonly string Url;
        public readonly string Sha256;

        public ScriptModelFile(string file, string url, string sha256) => (File, Url, Sha256) = (file, url, sha256);

        public string Path => System.IO.Path.Combine(Application.streamingAssetsPath, File);
        public bool Installed => System.IO.File.Exists(Path);
    }

    [DisallowMultipleComponent]
    public sealed class LocalScriptModel : MonoBehaviour, IScriptModel
    {
        // Qwen2.5-Coder-1.5B-Instruct, Apache-2.0, about 1.1 GB. Too weak to correct itself from an error (2026-09-25).
        public static readonly ScriptModelFile QwenCoder15B = new("Models/qwen2.5-coder-1.5b-instruct-q4_k_m.gguf",
            "https://huggingface.co/Qwen/Qwen2.5-Coder-1.5B-Instruct-GGUF/resolve/main/qwen2.5-coder-1.5b-instruct-q4_k_m.gguf",
            "cc324af070c2ecbfd324a30884d2f951a7ff756aba85cb811a6ec436933bb046");

        // Qwen3-4B-Instruct-2507 (non-thinking), Apache-2.0, about 2.5 GB; Q4_K_M quantization published by Unsloth.
        public static readonly ScriptModelFile Qwen3Instruct4B = new("Models/Qwen3-4B-Instruct-2507-Q4_K_M.gguf",
            "https://huggingface.co/unsloth/Qwen3-4B-Instruct-2507-GGUF/resolve/main/Qwen3-4B-Instruct-2507-Q4_K_M.gguf",
            "3605803b982cb64aead44f6c1b2ae36e3acdb41d8e46c8a94c6533bc4c67e597");

        public static ScriptModelFile Default => QwenCoder15B;

        private LLM _llm;
        private LLMAgent _agent;

        // The model to load; set before the first Warm.
        public ScriptModelFile File { get; set; } = Default;

        // Creates the LLM on a child object the first time, then waits for it to load. Throws when the file is missing or
        // llama.cpp cannot start.
        public async Task Warm()
        {
            if (_agent == null)
            {
                if (!File.Installed)
                    throw new FileNotFoundException(
                        $"The script assistant model is not installed ({File.File}). In the Editor use FoodFactory > Download Script Assistant Model.");
                // LLM and LLMAgent read their settings in Awake, so they are configured on an inactive object.
                var host = new GameObject("ScriptAssistantModel");
                host.transform.SetParent(transform, false);
                host.SetActive(false);
                _llm = host.AddComponent<LLM>();
                _llm.model = File.Path;
                _llm.numGPULayers = 99; // LlamaLib falls back to the CPU when no GPU backend loads.
                _llm.contextSize = 8192;
                _agent = host.AddComponent<LLMAgent>();
                _agent.llm = _llm;
                _agent.systemPrompt = ScriptAssistant.SystemPrompt;
                _agent.temperature = 0.2f;
                _agent.numPredict = 768;
                host.SetActive(true);
            }
            await _llm.WaitUntilReady();
            if (_llm.failed) throw new System.InvalidOperationException("The script assistant model failed to load; see the log.");
        }

        public async Task Reset()
        {
            await Warm();
            await _agent.ClearHistory();
        }

        public async Task<string> Ask(string message)
        {
            await Warm();
            return await _agent.Chat(message) ?? "";
        }
    }
}
