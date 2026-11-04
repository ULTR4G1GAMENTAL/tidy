using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Birinci sahis bakici: WASD yuru, Shift kos, Bosluk zipla, fare ile bak, Esc fareyi birak.
[RequireComponent(typeof(CharacterController))]
public class ZooPlayer : MonoBehaviour
{
    public float walkSpeed = 5f;
    public float runSpeed = 9f;
    public float jumpHeight = 1.1f;
    public float lookSensitivity = 0.12f;

    CharacterController cc;
    Transform cam;
    float pitch;
    float vy;

    void Start()
    {
        cc = GetComponent<CharacterController>();
        var c = GetComponentInChildren<Camera>();
        if (c != null) cam = c.transform;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (!Application.isPlaying || cam == null) return;
        Vector2 move, look;
        bool sprint, jump, esc, click;
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        var ms = Mouse.current;
        if (kb == null || ms == null) return;
        move = new Vector2((kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0),
                           (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0));
        look = ms.delta.ReadValue() * lookSensitivity;
        sprint = kb.leftShiftKey.isPressed;
        jump = kb.spaceKey.wasPressedThisFrame;
        esc = kb.escapeKey.wasPressedThisFrame;
        click = ms.leftButton.wasPressedThisFrame;
#else
        move = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        look = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 2f;
        sprint = Input.GetKey(KeyCode.LeftShift);
        jump = Input.GetKeyDown(KeyCode.Space);
        esc = Input.GetKeyDown(KeyCode.Escape);
        click = Input.GetMouseButtonDown(0);
#endif
        if (esc) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        if (click && Cursor.lockState != CursorLockMode.Locked) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            transform.Rotate(0f, look.x, 0f);
            pitch = Mathf.Clamp(pitch - look.y, -80f, 80f);
            cam.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        Vector3 dir = transform.right * move.x + transform.forward * move.y;
        if (dir.sqrMagnitude > 1f) dir.Normalize();
        float speed = sprint ? runSpeed : walkSpeed;

        if (cc.isGrounded && vy < 0f) vy = -2f;
        if (jump && cc.isGrounded) vy = Mathf.Sqrt(jumpHeight * 2f * 9.81f);
        vy -= 9.81f * Time.deltaTime;
        cc.Move((dir * speed + Vector3.up * vy) * Time.deltaTime);
    }
}
