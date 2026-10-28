using UnityEngine;

[AddComponentMenu("")]
public sealed class CollisionDebugRelay : MonoBehaviour
{
    CollisionDebugMode owner;

    public void Bind(CollisionDebugMode mode)
    {
        owner = mode;
        hideFlags = HideFlags.HideAndDontSave;
    }

    void OnCollisionEnter(Collision collision)
    {
        if (owner == null)
            return;

        owner.ReportBallCollision(collision);
        owner.NoteCollision(GetComponent<Rigidbody>(), collision, false);
    }

    void OnCollisionStay(Collision collision)
    {
        if (owner != null)
            owner.NoteCollision(GetComponent<Rigidbody>(), collision, false);
    }

    void OnCollisionExit(Collision collision)
    {
        if (owner != null)
            owner.NoteCollision(GetComponent<Rigidbody>(), collision, true);
    }
}
