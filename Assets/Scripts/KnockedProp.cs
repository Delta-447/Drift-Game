using UnityEngine;

/// <summary>
/// An obstacle that the shield punted out of the way. It tumbles off on a
/// simple ballistic arc and deletes itself once it is well clear of the road,
/// so the player never sees it land or sit awkwardly on the verge.
/// </summary>
public class KnockedProp : MonoBehaviour
{
    Vector3 velocity;
    Vector3 spin;
    float life;
    float maxLife = 2.2f;

    /// <summary>
    /// Launch it. <paramref name="sideways"/> is the road's right vector, and
    /// <paramref name="dir"/> is -1 or 1 for which side it flies off toward.
    /// </summary>
    public static void Launch(GameObject go, Vector3 forward, Vector3 sideways,
                              float dir, float speed)
    {
        if (go == null) return;
        go.transform.SetParent(null, true);   // survive its chunk being pruned

        var k = go.AddComponent<KnockedProp>();
        // mostly sideways and up, with a little of the car's momentum
        k.velocity = sideways * dir * speed * Random.Range(0.9f, 1.25f)
                   + Vector3.up * speed * Random.Range(0.5f, 0.8f)
                   + forward * speed * Random.Range(0.15f, 0.4f);
        k.spin = new Vector3(Random.Range(-540f, 540f),
                             Random.Range(-540f, 540f),
                             Random.Range(-540f, 540f));

        // strip anything that could still interact with the player
        var colliders = go.GetComponentsInChildren<Collider>();
        for (int i = 0; i < colliders.Length; i++) colliders[i].enabled = false;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        life += dt;

        velocity += Vector3.up * -18f * dt;          // punchy, arcade gravity
        transform.position += velocity * dt;
        transform.Rotate(spin * dt, Space.World);

        // shrink away at the end so it never visibly hits the ground
        float fade = Mathf.Clamp01((life - (maxLife - 0.5f)) / 0.5f);
        if (fade > 0f) transform.localScale *= Mathf.Max(0.01f, 1f - fade * dt * 8f);

        if (life >= maxLife) Destroy(gameObject);
    }
}
