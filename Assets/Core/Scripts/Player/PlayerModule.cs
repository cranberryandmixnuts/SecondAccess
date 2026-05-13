using UnityEngine;

[RequireComponent(typeof(Player))]
public abstract class PlayerModule : MonoBehaviour
{
    protected Player Player;

    protected virtual void Awake()
    {
        Player = GetComponent<Player>();
        ModuleAwake();
    }

    protected virtual void ModuleAwake() { }
}
