#if UNITY_IOS && !UNITY_EDITOR
using UnityEngine;
using System.Collections;

sealed class UnityOpenXR_StartupSilencer_iOS : MonoBehaviour, ILogHandler
{
    ILogHandler _inner;
    static bool _installed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
    static void Install()
    {
        if (_installed) return;
        _installed = true;

        var go = new GameObject(nameof(UnityOpenXR_StartupSilencer_iOS));
        Object.DontDestroyOnLoad(go);

        var s = go.AddComponent<UnityOpenXR_StartupSilencer_iOS>();
        s._inner = Debug.unityLogger.logHandler;

        // Replace logger with our filter, then restore shortly.
        Debug.unityLogger.logHandler = s;
        s.StartCoroutine(s.RestoreLoggerSoon()); // <-- FIX: call on MonoBehaviour, not GameObject
    }

    IEnumerator RestoreLoggerSoon()
    {
        // Give OpenXR's static init a moment to fail silently.
        yield return new WaitForSeconds(1f);

        if (Debug.unityLogger.logHandler == this)
            Debug.unityLogger.logHandler = _inner;

        Destroy(gameObject);
    }

    static bool IsOpenXRBindFail(string msg)
    {
        if (string.IsNullOrEmpty(msg)) return false;
        if (msg.IndexOf("UnityOpenXR", System.StringComparison.Ordinal) < 0) return false;

        return msg.IndexOf("DllNotFoundException", System.StringComparison.Ordinal) >= 0
             || msg.IndexOf("Unable to load DLL", System.StringComparison.Ordinal) >= 0
             || msg.IndexOf("dlopen(", System.StringComparison.Ordinal) >= 0;
    }

    public void LogFormat(LogType logType, Object ctx, string fmt, params object[] args)
    {
        if (logType == LogType.Exception)
        {
            string msg = (args == null || args.Length == 0) ? fmt : string.Format(fmt, args);
            if (IsOpenXRBindFail(msg)) return; // drop only the UnityOpenXR dll noise
        }
        _inner.LogFormat(logType, ctx, fmt, args);
    }

    public void LogException(System.Exception ex, Object ctx)
    {
        // Guard the real exception object too
        if (ex is System.DllNotFoundException d && d.Message.Contains("UnityOpenXR")) return;
        if (ex != null && IsOpenXRBindFail(ex.ToString())) return;

        _inner.LogException(ex, ctx);
    }
}
#endif