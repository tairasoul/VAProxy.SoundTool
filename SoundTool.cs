using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using VASoundTool.Patches;
using VASoundTool.Utilities;

namespace VASoundTool
{
    [BepInPlugin("tairasoul.vasoundtool", "VASoundTool", "1.0.0")]
    public class SoundTool : BaseUnityPlugin
    {
        private ConfigEntry<float> configPlayOnAwakePatchRepeatDelay;

        private readonly Harmony harmony = new("tairasoul.vasoundtool");

        public static SoundTool Instance;

        internal ManualLogSource logger;

        public KeyboardShortcut toggleAudioSourceDebugLog;
        public KeyboardShortcut toggleIndepthDebugLog;
        public KeyboardShortcut toggleInformationalDebugLog;
        public bool wasKeyDown;
        public bool wasKeyDown2;
        public bool wasKeyDown3;

        public static bool debugAudioSources;
        public static bool indepthDebugging;
        public static bool infoDebugging;

        public static bool IsDebuggingOn()
        {
            if (debugAudioSources || indepthDebugging || infoDebugging)
                return true;
            else
                return false;
        }

        public static Dictionary<string, List<RandomAudioClip>> replacedClips { get; private set; }

        public static Dictionary<string, AudioType> clipTypes { get; private set; }

        public enum AudioType { wav, ogg, mp3 }

        #region UNITY METHODS
        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            configPlayOnAwakePatchRepeatDelay = Config.Bind("Experimental", "NewPlayOnAwakePatchRepeatDelay", 90f, "How long to wait between checks for new playOnAwake AudioSources. Runs the same patching that is done when each scene is loaded with this delay between each run. DO NOT set too low or high. Anything below 10 or above 600 can cause issues. This time is in seconds. Set to 0 to disable rerunning the patch, but be warned that this might break runtime initialized playOnAwake AudioSources.");

            // NetcodePatcher stuff
            var types = Assembly.GetExecutingAssembly().GetTypes();
            foreach (var type in types)
            {
                var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                foreach (var method in methods)
                {
                    var attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
                    if (attributes.Length > 0)
                    {
                        method.Invoke(null, null);
                    }
                }
            }

            logger = BepInEx.Logging.Logger.CreateLogSource("VASoundTool");

            logger.LogInfo($"Plugin VASoundTool is loaded!");

            toggleAudioSourceDebugLog = new KeyboardShortcut(KeyCode.F5, new KeyCode[0]);
            toggleIndepthDebugLog = new KeyboardShortcut(KeyCode.F5, [KeyCode.LeftAlt]);
            toggleInformationalDebugLog = new KeyboardShortcut(KeyCode.F5, [KeyCode.LeftControl]);

            debugAudioSources = false;
            indepthDebugging = false;
            infoDebugging = false;

            replacedClips = [];
            clipTypes = [];
        }

        private void Start()
        {
            harmony.PatchAll(typeof(AudioSourcePatch));

            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void Update()
        {
            if (toggleInformationalDebugLog.IsDown() && !wasKeyDown3)
            {
                wasKeyDown3 = true;
                wasKeyDown2 = false;
                wasKeyDown = false;
            }
            if (toggleInformationalDebugLog.IsUp() && wasKeyDown3)
            {
                wasKeyDown3 = false;
                wasKeyDown2 = false;
                wasKeyDown = false;
                infoDebugging = !infoDebugging;
                Instance.logger.LogDebug($"Toggling informational debug logs {infoDebugging}!");
                return;
            }

            if (toggleIndepthDebugLog.IsDown() && !wasKeyDown2)
            {
                wasKeyDown2 = true;
                wasKeyDown = false;
            }
            if (toggleIndepthDebugLog.IsUp() && wasKeyDown2)
            {
                wasKeyDown2 = false;
                wasKeyDown = false;
                debugAudioSources = !debugAudioSources;
                indepthDebugging = debugAudioSources;
                infoDebugging = debugAudioSources;
                Instance.logger.LogDebug($"Toggling in-depth AudioSource debug logs {debugAudioSources}!");
                return;
            }

            if (!wasKeyDown2 && !toggleIndepthDebugLog.IsDown() && toggleAudioSourceDebugLog.IsDown() && !wasKeyDown)
            {
                wasKeyDown = true;
                wasKeyDown2 = false;
            }
            if (toggleAudioSourceDebugLog.IsUp() && wasKeyDown)
            {
                wasKeyDown = false;
                wasKeyDown2 = false;
                debugAudioSources = !debugAudioSources;
                if (indepthDebugging && !debugAudioSources)
                    indepthDebugging = false;
                Instance.logger.LogDebug($"Toggling AudioSource debug logs {debugAudioSources}!");
            }
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }
        #endregion

        #region SCENE LOAD SETUP METHODS
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (Instance == null)
                return;

            PatchPlayOnAwakeAudio(scene);

            if (scene.name != "Intro" && scene.name != "Menu")
            {
                StopAllCoroutines();
                StartCoroutine(PatchPlayOnAwakeDelayed(scene, 1f));
            }
        }

        private IEnumerator PatchPlayOnAwakeDelayed(Scene scene, float wait)
        {
            if (infoDebugging)
                logger.LogDebug($"Started playOnAwake patch coroutine with delay of {wait} seconds");
            yield return new WaitForSecondsRealtime(wait);
            if (infoDebugging)
                logger.LogDebug($"Running playOnAwake patch coroutine!");

            PatchPlayOnAwakeAudio(scene);

            float repeatWait = configPlayOnAwakePatchRepeatDelay.Value;

            if (repeatWait != 0f)
            {
                if (repeatWait < 10f)
                    repeatWait = 10f;
                if (repeatWait > 600f)
                    repeatWait = 600f;

                StartCoroutine(PatchPlayOnAwakeDelayed(scene, repeatWait));
            }
        }

        private void PatchPlayOnAwakeAudio(Scene scene)
        {
            if (infoDebugging)
                Instance.logger.LogDebug($"Grabbing all playOnAwake AudioSources for loaded scene {scene.name}");

            AudioSource[] sources = GetAllPlayOnAwakeAudioSources();

            if (infoDebugging)
            {
                Instance.logger.LogDebug($"Found a total of {sources.Length} playOnAwake AudioSource(s)!");
                Instance.logger.LogDebug($"Starting setup on {sources.Length} playOnAwake AudioSource(s)...");
            }

            foreach (AudioSource s in sources)
            {
                s.Stop();

                if (s.transform.TryGetComponent(out AudioSourceExtension sExt))
                {
                    sExt.audioSource = s;
                    sExt.playOnAwake = true;
                    sExt.loop = s.loop;
                    s.playOnAwake = false;
                    if (infoDebugging)
                        Instance.logger.LogDebug($"-Set- {System.Array.IndexOf(sources, s) + 1} {s} done!");
                }
                else
                {
                    AudioSourceExtension sExtNew = s.gameObject.AddComponent<AudioSourceExtension>();
                    sExtNew.audioSource = s;
                    sExtNew.playOnAwake = true;
                    sExtNew.loop = s.loop;
                    s.playOnAwake = false;
                    if (infoDebugging)
                        Instance.logger.LogDebug($"-Add- {System.Array.IndexOf(sources, s) + 1} {s} done!");
                }
            }

            if (infoDebugging)
                Instance.logger.LogDebug($"Done setting up {sources.Length} playOnAwake AudioSources!");
        }

        public AudioSource[] GetAllPlayOnAwakeAudioSources()
        {
            AudioSource[] sources = FindObjectsOfType<AudioSource>(true);
            List<AudioSource> results = new List<AudioSource>();

            for (int i = 0; i < sources.Length; i++)
            {
                if (sources[i].playOnAwake)
                {
                    results.Add(sources[i]);
                }
            }

            return [.. results];
        }
        #endregion

        #region REPLACEMENT METHODS
        public static void ReplaceAudioClip(string originalName, AudioClip newClip)
        {
            if (string.IsNullOrEmpty(originalName))
            {
                Instance.logger.LogWarning($"Plugin VASoundTool is trying to replace an audio clip without original clip specified! This is not allowed.");
                return;
            }
            if (newClip == null)
            {
                Instance.logger.LogWarning($"Plugin VASoundTool is trying to replace an audio clip without new clip specified! This is not allowed.");
                return;
            }

            string clipName = newClip.name;
            float chance = 100f;

            // If clipName contains "-number", parse the chance
            if (clipName.Contains("-"))
            {
                string[] parts = clipName.Split('-');
                if (parts.Length > 1)
                {
                    string lastPart = parts[parts.Length - 1];
                    if (int.TryParse(lastPart, out int parsedChance))
                    {
                        chance = parsedChance * 0.01f;
                        clipName = string.Join("-", parts, 0, parts.Length - 1);
                    }
                }
            }

            if (replacedClips.ContainsKey(originalName) && chance >= 100f)
            {
                Instance.logger.LogWarning($"Plugin VASoundTool is trying to replace an audio clip that already has been replaced with 100% chance of playback! This is not allowed.");
                return;
            }

            // Ensure the chance is within the valid range
            chance = Mathf.Clamp01(chance);

            // If the clipName already exists in the dictionary, add the new audio clip with its chance
            if (replacedClips.ContainsKey(originalName))
            {
                replacedClips[originalName].Add(new RandomAudioClip(newClip, chance));
            }
            // If the clipName doesn't exist, create a new entry in the dictionary
            else
            {
                replacedClips[originalName] = new List<RandomAudioClip> { new RandomAudioClip(newClip, chance) };
            }

            float totalChance = 0;

            for (int i = 0; i < replacedClips[originalName].Count(); i++)
            {
                totalChance += replacedClips[originalName][i].chance;
            }

            if ((totalChance < 1f || totalChance > 1f) && replacedClips[originalName].Count() > 1)
            {
                Instance.logger.LogDebug($"The current total combined chance for replaced {replacedClips[originalName].Count()} random audio clips for audio clip {originalName} does not equal 100% (at least yet?)");
            } else if (totalChance == 1f && replacedClips[originalName].Count() > 1)
            {
                Instance.logger.LogDebug($"The current total combined chance for replaced {replacedClips[originalName].Count()} random audio clips for audio clip {originalName} is equal to 100%");
            }

            //replacedClips.Add(originalName, newClip);
        }

        public static void ReplaceAudioClip(AudioClip originalClip, AudioClip newClip)
        {
            if (originalClip == null)
            {
                Instance.logger.LogWarning($"Plugin VASoundTool is trying to replace an audio clip without original clip specified! This is not allowed.");
                return;
            }
            if (newClip == null)
            {
                Instance.logger.LogWarning($"Plugin VASoundTool is trying to replace an audio clip without new clip specified! This is not allowed.");
                return;
            }

            ReplaceAudioClip(originalClip.name, newClip);
        }

        public static void ReplaceAudioClip(string originalName, AudioClip newClip, float chance)
        {
            if (string.IsNullOrEmpty(originalName))
            {
                Instance.logger.LogWarning($"Plugin VASoundTool is trying to replace an audio clip without original clip specified! This is not allowed.");
                return;
            }
            if (newClip == null)
            {
                Instance.logger.LogWarning($"Plugin VASoundTool is trying to replace an audio clip without new clip specified! This is not allowed.");
                return;
            }

            string clipName = newClip.name;

            if (replacedClips.ContainsKey(originalName) && chance >= 100f)
            {
                Instance.logger.LogWarning($"Plugin VASoundTool is trying to replace an audio clip that already has been replaced with 100% chance of playback! This is not allowed.");
                return;
            }

            // Ensure the chance is within the valid range
            chance = Mathf.Clamp01(chance);

            // If the clipName already exists in the dictionary, add the new audio clip with its chance
            if (replacedClips.ContainsKey(originalName))
            {
                replacedClips[originalName].Add(new RandomAudioClip(newClip, chance));
            }
            // If the clipName doesn't exist, create a new entry in the dictionary
            else
            {
                replacedClips[originalName] = new List<RandomAudioClip> { new RandomAudioClip(newClip, chance) };
            }

            float totalChance = 0;

            for (int i = 0; i < replacedClips[originalName].Count(); i++)
            {
                totalChance += replacedClips[originalName][i].chance;
            }

            if ((totalChance < 1f || totalChance > 1f) && replacedClips[originalName].Count() > 1)
            {
                Instance.logger.LogDebug($"The current total combined chance for replaced {replacedClips[originalName].Count()} random audio clips for audio clip {originalName} does not equal 100% (at least yet?)");
            }
            else if (totalChance == 1f && replacedClips[originalName].Count() > 1)
            {
                Instance.logger.LogDebug($"The current total combined chance for replaced {replacedClips[originalName].Count()} random audio clips for audio clip {originalName} is equal to 100%");
            }

            //replacedClips.Add(originalName, newClip);
        }

        public static void ReplaceAudioClip(AudioClip originalClip, AudioClip newClip, float chance)
        {
            if (originalClip == null)
            {
                Instance.logger.LogWarning($"Plugin VASoundTool is trying to replace an audio clip without original clip specified! This is not allowed.");
                return;
            }
            if (newClip == null)
            {
                Instance.logger.LogWarning($"Plugin VASoundTool is trying to replace an audio clip without new clip specified! This is not allowed.");
                return;
            }

            ReplaceAudioClip(originalClip.name, newClip, chance);
        }

        public static void RemoveRandomAudioClip(string name, float chance)
        {
            if (string.IsNullOrEmpty(name))
            {
                Instance.logger.LogWarning($"Plugin VASoundTool is trying to restore an audio clip without original clip specified! This is not allowed.");
                return;
            }

            if (!replacedClips.ContainsKey(name))
            {
                Instance.logger.LogWarning($"Plugin VASoundTool is trying to restore an audio clip that does not exist! This is not allowed.");
                return;
            }

            if (chance > 0f)
            {
                for (int i = 0; i < replacedClips[name].Count(); i++)
                {
                    if (replacedClips[name][i].chance == chance)
                    {
                        replacedClips[name].RemoveAt(i);
                        break;
                    }
                }
            }
        }

        public static void RestoreAudioClip(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                Instance.logger.LogWarning($"Plugin VASoundTool is trying to restore an audio clip without original clip specified! This is not allowed.");
                return;
            }

            if (!replacedClips.ContainsKey(name))
            {
                Instance.logger.LogWarning($"Plugin VASoundTool is trying to restore an audio clip that does not exist! This is not allowed.");
                return;
            }

            replacedClips.Remove(name);
        }

        public static void RestoreAudioClip(AudioClip clip)
        {
            if (clip == null)
            {
                Instance.logger.LogWarning($"Plugin VASoundTool is trying to restore an audio clip without original clip specified! This is not allowed.");
                return;
            }

            RestoreAudioClip(clip.name);
        }
        #endregion

        #region CLIP LOADING METHODS
        public static AudioClip GetAudioClip(string modFolder, string soundName)
        {
            return GetAudioClip(modFolder, string.Empty, soundName);
        }

        public static AudioClip GetAudioClip(string modFolder, string subFolder, string soundName)
        {
            AudioType audioType = AudioType.wav;
            bool tryLoading = true;
            string legacy = " ";

            // path stuff
            var path = Path.Combine(Paths.PluginPath, modFolder, subFolder, soundName);
            var pathOmitSubDir = Path.Combine(Paths.PluginPath, modFolder, soundName);
            var pathDir = Path.Combine(Paths.PluginPath, modFolder, subFolder);

            var pathLegacy = Path.Combine(Paths.PluginPath, subFolder, soundName);
            var pathDirLegacy = Path.Combine(Paths.PluginPath, subFolder);

            // check if file and directory are valid, else skip loading
            if (!Directory.Exists(pathDir))
            {
                if (!string.IsNullOrEmpty(subFolder))
                    Instance.logger.LogWarning($"Requested directory at BepInEx/Plugins/{modFolder}/{subFolder} does not exist!");
                else
                {
                    Instance.logger.LogWarning($"Requested directory at BepInEx/Plugins/{modFolder} does not exist!");
                    if (!modFolder.Contains("-"))
                        Instance.logger.LogWarning($"This sound mod might not be compatable with mod managers. You should contact the sound mod's author.");
                }
                //Directory.CreateDirectory(pathDir);
                tryLoading = false;
            }
            if (!File.Exists(path))
            {
                Instance.logger.LogWarning($"Requested audio file does not exist at path {path}!");
                tryLoading = false;

                Instance.logger.LogDebug($"Looking for audio file from mod root instead at {pathOmitSubDir}...");
                if (File.Exists(pathOmitSubDir))
                {
                    Instance.logger.LogDebug($"Found audio file at path {pathOmitSubDir}!");
                    path = pathOmitSubDir;
                    tryLoading = true;
                }
                else
                {
                    Instance.logger.LogWarning($"Requested audio file does not exist at mod root path {pathOmitSubDir}!");
                }
            }
            if (Directory.Exists(pathDirLegacy))
            {
                if (!string.IsNullOrEmpty(subFolder))
                    Instance.logger.LogWarning($"Legacy directory location at BepInEx/Plugins/{subFolder} found!");
                else if (!modFolder.Contains("-"))
                    Instance.logger.LogWarning($"Legacy directory location at BepInEx/Plugins found!");
            }
            if (File.Exists(pathLegacy))
            {
                Instance.logger.LogWarning($"Legacy path contains the requested audio file at path {pathLegacy}!");
                legacy = " legacy ";
                path = pathLegacy;
                tryLoading = true;
            }

            string[] parts = soundName.Split('.');
            if (parts[parts.Length - 1].ToLower().Contains("ogg"))
            {
                audioType = AudioType.ogg;
                Instance.logger.LogDebug($"File detected as an Ogg Vorbis file!");
            }
            else if (parts[parts.Length - 1].ToLower().Contains("mp3"))
            {
                audioType = AudioType.mp3;
                Instance.logger.LogDebug($"File detected as a MPEG MP3 file!");
            }

            AudioClip result = null;

            if (tryLoading)
            {
                Instance.logger.LogDebug($"Loading AudioClip {soundName} from{legacy}path: {path}");

                switch (audioType)
                {
                    case AudioType.wav:
                        result = WavUtility.LoadFromDiskToAudioClip(path);
                        break;
                    case AudioType.ogg:
                        result = OggUtility.LoadFromDiskToAudioClip(path);
                        break;
                    case AudioType.mp3:
                        result = Mp3Utility.LoadFromDiskToAudioClip(path);
                        break;
                }

                Instance.logger.LogDebug($"Finished loading AudioClip {soundName} with length of {result.length}!");
            }
            else
            {
                Instance.logger.LogWarning($"Failed to load AudioClip {soundName} from invalid{legacy}path at {path}!");
            }

            // Workaround to ensure the clip always gets named because for some reason Unity doesn't always get the name and leaves it blank sometimes???
            if (string.IsNullOrEmpty(result.name))
            {
                string finalName = string.Empty;
                string[] nameParts = new string[0];

                switch (audioType)
                {
                    case AudioType.wav:

                        finalName = soundName.Replace(".wav", "");

                        nameParts = finalName.Split('/');

                        if (nameParts.Length <= 1)
                        {
                            nameParts = finalName.Split('\\');
                        }

                        finalName = nameParts[nameParts.Length - 1];

                        result.name = finalName;
                        break;
                    case AudioType.ogg:
                        finalName = soundName.Replace(".ogg", "");

                        nameParts = finalName.Split('/');

                        if (nameParts.Length <= 1)
                        {
                            nameParts = finalName.Split('\\');
                        }

                        finalName = nameParts[nameParts.Length - 1];

                        result.name = finalName;
                        break;
                    case AudioType.mp3:
                        finalName = soundName.Replace(".mp3", "");

                        nameParts = finalName.Split('/');

                        if (nameParts.Length <= 1)
                        {
                            nameParts = finalName.Split('\\');
                        }

                        finalName = nameParts[nameParts.Length - 1];

                        result.name = finalName;
                        break;
                }
            }

            if (result != null)
                clipTypes.Add(result.name, audioType);

            // return the clip we got
            return result;
        }

        public static AudioClip GetAudioClip(string modFolder, string soundName, AudioType audioType)
        {
            return GetAudioClip(modFolder, string.Empty, soundName, audioType);
        }

        public static AudioClip GetAudioClip(string modFolder, string subFolder, string soundName, AudioType audioType)
        {
            bool tryLoading = true;
            string legacy = " ";

            // path stuff
            var path = Path.Combine(Paths.PluginPath, modFolder, subFolder, soundName);
            var pathOmitSubDir = Path.Combine(Paths.PluginPath, modFolder, soundName);
            var pathDir = Path.Combine(Paths.PluginPath, modFolder, subFolder);

            var pathLegacy = Path.Combine(Paths.PluginPath, subFolder, soundName);
            var pathDirLegacy = Path.Combine(Paths.PluginPath, subFolder);

            // check if file and directory are valid, else skip loading
            if (!Directory.Exists(pathDir))
            {
                if (!string.IsNullOrEmpty(subFolder))
                    Instance.logger.LogWarning($"Requested directory at BepInEx/Plugins/{modFolder}/{subFolder} does not exist!");
                else
                {
                    Instance.logger.LogWarning($"Requested directory at BepInEx/Plugins/{modFolder} does not exist!");
                    if (!modFolder.Contains("-"))
                        Instance.logger.LogWarning($"This sound mod might not be compatable with mod managers. You should contact the sound mod's author.");
                }
                //Directory.CreateDirectory(pathDir);
                tryLoading = false;
            }
            if (!File.Exists(path))
            {
                Instance.logger.LogWarning($"Requested audio file does not exist at path {path}!");
                tryLoading = false;

                Instance.logger.LogDebug($"Looking for audio file from mod root instead at {pathOmitSubDir}...");
                if (File.Exists(pathOmitSubDir))
                {
                    Instance.logger.LogDebug($"Found audio file at path {pathOmitSubDir}!");
                    path = pathOmitSubDir;
                    tryLoading = true;
                }
                else
                {
                    Instance.logger.LogWarning($"Requested audio file does not exist at mod root path {pathOmitSubDir}!");
                }
            }
            if (Directory.Exists(pathDirLegacy))
            {
                if (!string.IsNullOrEmpty(subFolder))
                    Instance.logger.LogWarning($"Legacy directory location at BepInEx/Plugins/{subFolder} found!");
                else if (!modFolder.Contains("-"))
                    Instance.logger.LogWarning($"Legacy directory location at BepInEx/Plugins found!");
            }
            if (File.Exists(pathLegacy))
            {
                Instance.logger.LogWarning($"Legacy path contains the requested audio file at path {pathLegacy}!");
                legacy = " legacy ";
                path = pathLegacy;
                tryLoading = true;
            }

            switch (audioType)
            {
                case AudioType.wav:
                    Instance.logger.LogDebug($"File defined as a WAV file!");
                    break;
                case AudioType.ogg:
                    Instance.logger.LogDebug($"File defined as an Ogg Vorbis file!");
                    break;
                case AudioType.mp3:
                    Instance.logger.LogDebug($"File defined as a MPEG MP3 file!");
                    break;
            }

            AudioClip result = null;

            if (tryLoading)
            {
                Instance.logger.LogDebug($"Loading AudioClip {soundName} from{legacy}path: {path}");

                switch (audioType)
                {
                    case AudioType.wav:
                        result = WavUtility.LoadFromDiskToAudioClip(path);
                        break;
                    case AudioType.ogg:
                        result = OggUtility.LoadFromDiskToAudioClip(path);
                        break;
                    case AudioType.mp3:
                        result = Mp3Utility.LoadFromDiskToAudioClip(path);
                        break;
                }

                Instance.logger.LogDebug($"Finished loading AudioClip {soundName} with length of {result.length}!");
            }
            else
            {
                Instance.logger.LogWarning($"Failed to load AudioClip {soundName} from invalid{legacy}path at {path}!");
            }

            // Workaround to ensure the clip always gets named because for some reason Unity doesn't always get the name and leaves it blank sometimes???
            if (string.IsNullOrEmpty(result.name))
            {
                string finalName = string.Empty;
                string[] nameParts = new string[0];

                switch (audioType)
                {
                    case AudioType.wav:

                        finalName = soundName.Replace(".wav", "");

                        nameParts = finalName.Split('/');

                        if (nameParts.Length <= 1)
                        {
                            nameParts = finalName.Split('\\');
                        }

                        finalName = nameParts[nameParts.Length - 1];

                        result.name = finalName;
                        break;
                    case AudioType.ogg:
                        finalName = soundName.Replace(".ogg", "");

                        nameParts = finalName.Split('/');

                        if (nameParts.Length <= 1)
                        {
                            nameParts = finalName.Split('\\');
                        }

                        finalName = nameParts[nameParts.Length - 1];

                        result.name = finalName;
                        break;
                    case AudioType.mp3:
                        finalName = soundName.Replace(".mp3", "");

                        nameParts = finalName.Split('/');

                        if (nameParts.Length <= 1)
                        {
                            nameParts = finalName.Split('\\');
                        }

                        finalName = nameParts[nameParts.Length - 1];

                        result.name = finalName;
                        break;
                }
            }

            if (result != null)
                clipTypes.Add(result.name, audioType);

            // return the clip we got
            return result;
        }
        #endregion
    }
}
