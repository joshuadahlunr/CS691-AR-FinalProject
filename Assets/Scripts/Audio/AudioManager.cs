using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public class AudioManager : AudioManagerBase {
	// List of music clips
	public NamedAudioClip[] musicClips;
	// List of ui sound effect clips
	public NamedAudioClip[] uiSoundFxClips;
	// List of sound effect clips
	public NamedAudioClip[] soundFxClips;

	// References to the players we create
	public AudioManagerBase.AudioPlayer musicPlayer, uiSoundFXPlayer, soundFXPlayer;

	// Override instance to represent the Whitehat type
	public new static AudioManager instance => AudioManagerBase.instance as AudioManager;
	
	public AudioMixerGroup mixerGroup;

	private void Start(){
		// Create a music player and set it off cycling through the music tracks indefinitely
		musicPlayer = CreateAudioPlayer("music", musicClips);
		musicPlayer.volume = .8f;
		musicPlayer.source.outputAudioMixerGroup = mixerGroup;
		musicPlayer.CycleTracks(/*once*/ false, 10);

		// Create a UI SoundFX player
		uiSoundFXPlayer = CreateAudioPlayer("uiSoundFX", uiSoundFxClips);
		uiSoundFXPlayer.source.outputAudioMixerGroup = mixerGroup;
		uiSoundFXPlayer.source.loop = false;

		// Create a SoundFX player
		soundFXPlayer = CreateAudioPlayer("soundFX", soundFxClips);
		soundFXPlayer.source.outputAudioMixerGroup = mixerGroup;
		soundFXPlayer.source.loop = false;
	}
}
