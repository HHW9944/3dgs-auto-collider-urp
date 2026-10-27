using UnityEditor;
using UnityEngine;

public static class CollisionTestSetup
{
    [MenuItem("Gaussian Tool/Create Collision Test Objects")]
    public static void Create()
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "CollisionTestGround";
        ground.transform.position = new Vector3(0f, -0.5f, 0f);
        ground.transform.localScale = new Vector3(20f, 1f, 20f);
        ground.isStatic = true;

        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "CollisionTestSphere";
        sphere.transform.position = new Vector3(0f, 8f, 0f);
        Rigidbody body = sphere.AddComponent<Rigidbody>();
        body.mass = 1f;
        body.collisionDetectionMode = CollisionDetectionMode.Continuous;

        Undo.RegisterCreatedObjectUndo(ground, "Create Collision Test Objects");
        Undo.RegisterCreatedObjectUndo(sphere, "Create Collision Test Objects");
        Selection.activeGameObject = sphere;
    }
}
