using Comfort.Common;
using EFT;
using EFT.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Blackhorse311.BotMind.Modules.MedicBuddy
{
    /// <summary>
    /// Loads and plays .ogg voice line audio files for MedicBuddy events.
    /// Supports bilingual audio: English (USEC) and Russian (BEAR/Scav).
    /// Files are loaded from BepInEx/plugins/Blackhorse311-BotMind/voicelines/en/ and /ru/.
    /// Each event supports up to 5 variants: {event}_{1-5}.ogg.
    /// All files are optional - missing files are silently skipped.
    /// </summary>
    public static class MedicBuddyAudio
    {
        private const int VARIANTS_PER_EVENT = 5;
        private const string VOICELINES_FOLDER = "voicelines";
        private const string LANG_EN = "en";
        private const string LANG_RU = "ru";

        /// <summary>Language -> event name -> array of loaded clips (null entries for missing files).</summary>
        private static readonly Dictionary<string, Dictionary<string, AudioClip[]>> _clips =
            new Dictionary<string, Dictionary<string, AudioClip[]>>();

        private static bool _initialized;
        private static string _voicelinesPath;

        /// <summary>All MedicBuddy audio event names.</summary>
        private static readonly string[] EventNames =
        {
            "summon_request",
            "team_enroute",
            "team_arrived",
            "healing_start",
            "healing_complete",
            "team_exfil"
        };

        private static readonly string[] Languages = { LANG_EN, LANG_RU };

        /// <summary>
        /// Initializes the audio system by loading all available .ogg voice lines.
        /// Scans both en/ and ru/ subfolders under voicelines/.
        /// Call from BotMindPlugin when MedicBuddy is enabled.
        /// </summary>
        /// <param name="coroutineRunner">A MonoBehaviour to run the async loading coroutine on.</param>
        public static void Initialize(MonoBehaviour coroutineRunner)
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                string pluginDir = Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
                _voicelinesPath = Path.Combine(pluginDir, VOICELINES_FOLDER);

                if (!Directory.Exists(_voicelinesPath))
                {
                    BotMindPlugin.Log?.LogInfo(
                        $"MedicBuddy voicelines folder not found at: {_voicelinesPath} " +
                        "(voice lines disabled - text notifications will still display)");
                    return;
                }

                // Load clips from each language subfolder
                foreach (string lang in Languages)
                {
                    string langPath = Path.Combine(_voicelinesPath, lang);
                    if (!Directory.Exists(langPath))
                    {
                        BotMindPlugin.Log?.LogDebug($"MedicBuddy voicelines/{lang}/ folder not found - skipping");
                        continue;
                    }

                    BotMindPlugin.Log?.LogDebug($"Loading MedicBuddy {lang} voice lines from: {langPath}");

                    _clips[lang] = new Dictionary<string, AudioClip[]>();

                    foreach (string eventName in EventNames)
                    {
                        _clips[lang][eventName] = new AudioClip[VARIANTS_PER_EVENT];

                        for (int i = 0; i < VARIANTS_PER_EVENT; i++)
                        {
                            string fileName = $"{eventName}_{i + 1}.ogg";
                            string filePath = Path.Combine(langPath, fileName);

                            if (File.Exists(filePath))
                            {
                                coroutineRunner.StartCoroutine(LoadClip(lang, eventName, i, filePath));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogWarning(
                    $"MedicBuddy audio initialization failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Plays the voice line for the given event at the specified variant index.
        /// Selects language based on player faction (USEC = English, BEAR/Scav = Russian).
        /// Falls back to the other language if preferred language has no clips.
        /// Does nothing if no clips are loaded or voice is disabled.
        /// </summary>
        /// <param name="eventName">The event name (e.g., "summon_request").</param>
        /// <param name="variantIndex">Preferred variant index (0-4), matched to notification text.</param>
        /// <param name="side">Player faction for language selection.</param>
        public static void Play(string eventName, int variantIndex, EPlayerSide side)
        {
            try
            {
                if (!Configuration.BotMindConfig.MedicBuddyEnableVoice.Value)
                    return;

                string preferredLang = (side == EPlayerSide.Usec) ? LANG_EN : LANG_RU;
                string fallbackLang = (side == EPlayerSide.Usec) ? LANG_RU : LANG_EN;

                AudioClip clip = FindClip(preferredLang, eventName, variantIndex)
                              ?? FindClip(fallbackLang, eventName, variantIndex);

                if (clip == null) return;

                // Play via GUISounds (non-spatial UI audio)
                float volume = Configuration.BotMindConfig.MedicBuddyVoiceVolume.Value / 100f;
                var guiSounds = Singleton<GUISounds>.Instance;
                if (guiSounds != null)
                {
                    guiSounds.PlaySound(clip, false, true, volume);
                }
                else
                {
                    BotMindPlugin.Log?.LogDebug("GUISounds not available for voice line playback");
                }
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogDebug($"Voice line playback failed for {eventName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Cleans up all loaded audio clips to free memory.
        /// Call from BotMindPlugin on game world destroyed.
        /// </summary>
        public static void Cleanup()
        {
            foreach (var langKvp in _clips)
            {
                foreach (var eventKvp in langKvp.Value)
                {
                    for (int i = 0; i < eventKvp.Value.Length; i++)
                    {
                        if (eventKvp.Value[i] != null)
                        {
                            UnityEngine.Object.Destroy(eventKvp.Value[i]);
                            eventKvp.Value[i] = null;
                        }
                    }
                }
            }
            _clips.Clear();
            _initialized = false;

            BotMindPlugin.Log?.LogDebug("MedicBuddy audio clips cleaned up");
        }

        /// <summary>
        /// Tries to find a clip for the given language, event, and variant.
        /// Falls back to any available variant if the preferred one is missing.
        /// </summary>
        private static AudioClip FindClip(string lang, string eventName, int variantIndex)
        {
            if (!_clips.TryGetValue(lang, out var langClips))
                return null;

            if (!langClips.TryGetValue(eventName, out AudioClip[] clips))
                return null;

            // Try the preferred variant first (synced with notification text)
            if (variantIndex >= 0 && variantIndex < clips.Length && clips[variantIndex] != null)
            {
                return clips[variantIndex];
            }

            // Fallback: find any available clip for this event in this language
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null)
                    return clips[i];
            }

            return null;
        }

        /// <summary>
        /// Coroutine that loads a single .ogg file as an AudioClip.
        /// </summary>
        private static IEnumerator LoadClip(string lang, string eventName, int index, string filePath)
        {
            // Unity requires file:// URI for local files
            string uri = "file:///" + filePath.Replace('\\', '/');

            using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.OGGVORBIS))
            {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.ConnectionError ||
                    request.result == UnityWebRequest.Result.ProtocolError)
                {
                    BotMindPlugin.Log?.LogDebug(
                        $"Failed to load voice line {Path.GetFileName(filePath)}: {request.error}");
                    yield break;
                }

                try
                {
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
                    if (clip != null)
                    {
                        clip.name = $"MedicBuddy_{lang}_{eventName}_{index + 1}";
                        _clips[lang][eventName][index] = clip;
                        BotMindPlugin.Log?.LogDebug($"Loaded voice line: {clip.name}");
                    }
                }
                catch (Exception ex)
                {
                    BotMindPlugin.Log?.LogDebug(
                        $"Error processing voice line {Path.GetFileName(filePath)}: {ex.Message}");
                }
            }
        }
    }
}
