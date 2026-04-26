using System;
using UnityEngine;

public class SceneUnloader : MonoBehaviour
{
    public void Unload() {
        Destroy(gameObject);
    }
}
