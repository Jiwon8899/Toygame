using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace PickAndPlaceShop
{
    public enum ShopNarrativeResultKind
    {
        Api,
        Cache,
        Disabled,
        MissingKey,
        RateLimited,
        Timeout,
        RequestFailed,
        InvalidResponse
    }

    public readonly struct ShopNarrativeResult
    {
        public readonly ShopNarrativeResultKind Kind;
        public readonly string Text;

        public ShopNarrativeResult(ShopNarrativeResultKind kind, string text)
        {
            Kind = kind;
            Text = text ?? string.Empty;
        }

        public bool HasText => !string.IsNullOrWhiteSpace(Text);
        public bool IsApiSuccess => Kind == ShopNarrativeResultKind.Api || Kind == ShopNarrativeResultKind.Cache;
    }

    public sealed class ShopNarrativeAIService : MonoBehaviour
    {
        [Serializable]
        private sealed class AnthropicMessage
        {
            public string role;
            public string content;
        }

        [Serializable]
        private sealed class AnthropicRequest
        {
            public string model;
            public int max_tokens;
            public string system;
            public AnthropicMessage[] messages;
        }

        [Serializable]
        private sealed class AnthropicContent
        {
            public string type;
            public string text;
        }

        [Serializable]
        private sealed class AnthropicUsage
        {
            public int input_tokens;
            public int output_tokens;
        }

        [Serializable]
        private sealed class AnthropicResponse
        {
            public AnthropicContent[] content;
            public AnthropicUsage usage;
        }

        private static ShopNarrativeAIService instance;
        private readonly Dictionary<string, string> responseCache = new(StringComparer.Ordinal);
        private readonly Queue<float> requestTimes = new();
        private float lastRequestTime = float.NegativeInfinity;

        public static ShopNarrativeAIService Instance => instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject host = new("Shop Narrative AI Service");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<ShopNarrativeAIService>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void Request(string contextKey, string prompt, Action<ShopNarrativeResult> completed)
        {
            ShopOperationsConfig config = ShopOperationsConfig.Load();
            if (config == null || !config.NarrativeAIEnabled)
            {
                completed?.Invoke(new ShopNarrativeResult(ShopNarrativeResultKind.Disabled, string.Empty));
                return;
            }
            if (string.IsNullOrWhiteSpace(contextKey) || string.IsNullOrWhiteSpace(prompt))
            {
                completed?.Invoke(new ShopNarrativeResult(ShopNarrativeResultKind.InvalidResponse, string.Empty));
                return;
            }
            if (responseCache.TryGetValue(contextKey, out string cached))
            {
                completed?.Invoke(new ShopNarrativeResult(ShopNarrativeResultKind.Cache, cached));
                return;
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (config.NarrativeEndpoint.StartsWith("qa-timeout://", StringComparison.Ordinal))
            {
                StartCoroutine(SimulatedTimeoutRoutine(config.NarrativeTimeoutSeconds, completed));
                return;
            }
#endif

            string apiKey = Environment.GetEnvironmentVariable(config.NarrativeApiKeyEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                completed?.Invoke(new ShopNarrativeResult(ShopNarrativeResultKind.MissingKey, string.Empty));
                return;
            }
            if (!TryReserveRateLimit(config))
            {
                completed?.Invoke(new ShopNarrativeResult(ShopNarrativeResultKind.RateLimited, string.Empty));
                return;
            }

            StartCoroutine(RequestRoutine(config, apiKey, contextKey, prompt, completed));
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static IEnumerator SimulatedTimeoutRoutine(float seconds,
            Action<ShopNarrativeResult> completed)
        {
            yield return new WaitForSecondsRealtime(Mathf.Max(1f, seconds));
            completed?.Invoke(new ShopNarrativeResult(ShopNarrativeResultKind.Timeout, string.Empty));
        }
#endif

        private bool TryReserveRateLimit(ShopOperationsConfig config)
        {
            float now = Time.realtimeSinceStartup;
            while (requestTimes.Count > 0 && now - requestTimes.Peek() >= 60f)
                requestTimes.Dequeue();
            float minimumInterval = 1f / config.NarrativeRequestsPerSecond;
            if (now - lastRequestTime < minimumInterval ||
                requestTimes.Count >= config.NarrativeRequestsPerMinute)
                return false;
            lastRequestTime = now;
            requestTimes.Enqueue(now);
            return true;
        }

        private IEnumerator RequestRoutine(ShopOperationsConfig config, string apiKey,
            string contextKey, string prompt, Action<ShopNarrativeResult> completed)
        {
            AnthropicRequest payload = new()
            {
                model = config.NarrativeModel,
                max_tokens = config.NarrativeMaxTokens,
                system = config.NarrativeSystemPrompt,
                messages = new[] { new AnthropicMessage { role = "user", content = prompt } }
            };
            byte[] body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));
            using UnityWebRequest request = new(config.NarrativeEndpoint, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(body),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = Mathf.CeilToInt(config.NarrativeTimeoutSeconds)
            };
            request.SetRequestHeader("content-type", "application/json");
            request.SetRequestHeader("x-api-key", apiKey);
            request.SetRequestHeader("anthropic-version", "2023-06-01");

            UnityWebRequestAsyncOperation operation;
            try
            {
                operation = request.SendWebRequest();
            }
            catch (Exception)
            {
                completed?.Invoke(new ShopNarrativeResult(
                    ShopNarrativeResultKind.RequestFailed, string.Empty));
                yield break;
            }
            float deadline = Time.realtimeSinceStartup + config.NarrativeTimeoutSeconds;
            while (!operation.isDone && Time.realtimeSinceStartup < deadline) yield return null;
            if (!operation.isDone)
            {
                request.Abort();
                completed?.Invoke(new ShopNarrativeResult(ShopNarrativeResultKind.Timeout, string.Empty));
                yield break;
            }
            if (request.result != UnityWebRequest.Result.Success)
            {
                ShopNarrativeResultKind kind = request.error != null &&
                                               request.error.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0
                    ? ShopNarrativeResultKind.Timeout
                    : ShopNarrativeResultKind.RequestFailed;
                completed?.Invoke(new ShopNarrativeResult(kind, string.Empty));
                yield break;
            }

            AnthropicResponse response;
            try
            {
                response = JsonUtility.FromJson<AnthropicResponse>(request.downloadHandler.text);
            }
            catch (Exception)
            {
                response = null;
            }
            string generated = ExtractText(response);
            if (string.IsNullOrWhiteSpace(generated))
            {
                completed?.Invoke(new ShopNarrativeResult(ShopNarrativeResultKind.InvalidResponse, string.Empty));
                yield break;
            }
            responseCache[contextKey] = generated;
            completed?.Invoke(new ShopNarrativeResult(ShopNarrativeResultKind.Api, generated));
        }

        private static string ExtractText(AnthropicResponse response)
        {
            if (response?.content == null) return string.Empty;
            for (int i = 0; i < response.content.Length; i++)
            {
                AnthropicContent block = response.content[i];
                if (block == null || block.type != "text" || string.IsNullOrWhiteSpace(block.text)) continue;
                string value = block.text.Replace('\r', ' ').Replace('\n', ' ').Trim();
                while (value.Contains("  ", StringComparison.Ordinal)) value = value.Replace("  ", " ");
                return value.Length <= 220 ? value : value.Substring(0, 220).TrimEnd() + "…";
            }
            return string.Empty;
        }
    }
}
