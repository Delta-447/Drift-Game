using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Flight recorder for the steering. EDITOR ONLY - it never starts in a
/// phone build. While a run is being driven it writes one line per frame
/// (thumb or mouse input, the slide, the body's angle, the camera, the frame
/// time) to SteeringLogs/ next to the Assets folder, so what the car is
/// actually doing can be read back afterwards. It only reads; it changes
/// nothing about how the game plays.
/// </summary>
public class DriveRecorder : MonoBehaviour
{
#if UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var car = Object.FindAnyObjectByType<CarController>();
        if (car == null) return;
        var go = new GameObject("DriveRecorder");
        go.AddComponent<DriveRecorder>().car = car;
    }
#endif

    public CarController car;

    const int KeepFiles = 5;          // older logs are overwritten in turn
    const float NewRunAfter = 1.0f;   // seconds without driving that end a log

    StringBuilder sb;
    float lastDrivenTime = -10f;
    int slot;

    void LateUpdate()
    {
        if (car == null) return;

        bool driven = car.LastTickFrame == Time.frameCount;
        if (!driven)
        {
            if (sb != null && Time.time - lastDrivenTime > NewRunAfter) Flush();
            return;
        }

        if (sb == null)
        {
            sb = new StringBuilder(1 << 20);
            sb.Append(CarController.DebugHeader)
              .Append(",camX,camY,camZ,camYaw,camPitch,camRoll\n");
        }
        lastDrivenTime = Time.time;

        sb.Append(car.DebugRow());
        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 p = cam.transform.position;
            Vector3 e = cam.transform.eulerAngles;
            sb.Append(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                ",{0:0.0000},{1:0.0000},{2:0.0000},{3:0.000},{4:0.000},{5:0.000}",
                p.x, p.y, p.z, e.y, e.x, e.z));
        }
        else sb.Append(",,,,,,");
        sb.Append('\n');

        // don't let one very long run eat memory
        if (sb.Length > 8 << 20) Flush();
    }

    void OnDisable() { Flush(); }
    void OnApplicationQuit() { Flush(); }

    void Flush()
    {
        if (sb == null) return;
        try
        {
            string dir = Path.Combine(Application.dataPath, "..", "SteeringLogs");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "drive_" + slot + ".csv");
            File.WriteAllText(file, sb.ToString());
            File.WriteAllText(Path.Combine(dir, "latest.txt"), "drive_" + slot + ".csv\n");
            slot = (slot + 1) % KeepFiles;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[DriveRecorder] could not save the log: " + ex.Message);
        }
        sb = null;
    }
}
