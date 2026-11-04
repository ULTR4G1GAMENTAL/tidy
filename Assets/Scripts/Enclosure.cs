using UnityEngine;

// Her kafes, tank ve teraryum bu bileşeni taşır. Kapı ve sayaç sistemi buna bağlanacak.
public class Enclosure : MonoBehaviour
{
    public string speciesId;
    public string displayName;
    public string kind;      // open, aviary, reptile, aquarium
    public int capacity = 18;
    public int placed;
}
