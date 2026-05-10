using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class MultipleImagesTrackingManager : MonoBehaviour {
    public List<GameObject> prefabsToSpawn = new();
    private ARTrackedImageManager trackedImageManager;
    private Dictionary<string, GameObject> arObjects;

    private void Start() {
        trackedImageManager = GetComponent<ARTrackedImageManager>();
        if (trackedImageManager == null) return;
        trackedImageManager.trackablesChanged.AddListener(OnImagesTrackedChanged);
        
        arObjects = new Dictionary<string, GameObject>();
        
        foreach (var prefab in prefabsToSpawn) {
            var arObject = Instantiate(prefab, Vector3.zero, Quaternion.identity);
            arObject.name = prefab.name;
            arObject.gameObject.SetActive(false);
            arObjects.Add(arObject.name, arObject);
        }
    }

    private void OnDestroy() {
        trackedImageManager.trackablesChanged.RemoveListener(OnImagesTrackedChanged);
    }

    private void OnImagesTrackedChanged(ARTrackablesChangedEventArgs<ARTrackedImage> args) {
        foreach (var image in args.added) UpdateTrackedImage(image); 
        foreach (var image in args.updated) UpdateTrackedImage(image); 
        foreach (var image in args.removed) UpdateTrackedImage(image.Value); 
    }

    private void UpdateTrackedImage(ARTrackedImage image) {
        if (image == null) return;
        
        var obj = arObjects[image.referenceImage.name];
        if (image.trackingState is TrackingState.Limited or TrackingState.None) {
            obj.SetActive(false);
            return;
        }
        
        obj.SetActive(true);
        obj.transform.position = image.transform.position;
        obj.transform.rotation = image.transform.rotation;
    }
}
