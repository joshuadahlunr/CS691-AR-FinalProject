using UnityEngine;

public class SetSceneAudio : MonoBehaviour {
    public AudioManagerBase.NamedAudioClip[] musicClips;
    public float fadeDuration = 3;
    
    public void OnEnable() {
        var audioManager = FindObjectsByType<AudioManager>(FindObjectsInactive.Include, FindObjectsSortMode.None)[0];
        audioManager.musicPlayer.ClearTracks();
        audioManager.musicPlayer.AddTracks(musicClips);
        audioManager.musicPlayer.CycleTracks(false, fadeDuration);
    }
}
