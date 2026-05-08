using UnityEngine;

public class GoToURL : MonoBehaviour {
    public string url;

    public void OpenURL() {
        Application.OpenURL(url);
    }
}
