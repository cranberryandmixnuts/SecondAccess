using UnityEngine;

public abstract class PlayerModule : MonoBehaviour
{
    public Player Player { get; private set; }

    public bool IsInitialized => Player != null;

    public void Initialize(Player player)
    {
        if (Player == player)
            return;

        Player = player;
        OnInitialized();
    }

    protected virtual void OnInitialized() { }
}
