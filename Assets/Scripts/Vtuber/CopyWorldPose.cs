using UnityEngine;

public class CopyWorldPose : MonoBehaviour {
    public PoseWorldMapper mapper;
    public int poseToCopy = 0;
    
    // Update is called once per frame
    private void Update() {
        transform.position = poseToCopy switch {
            -2 => mapper.HipCenter,
            -1 => mapper.ShoulderCenter,
            _ => mapper.WorldPositions[poseToCopy]
        };
    }
}
