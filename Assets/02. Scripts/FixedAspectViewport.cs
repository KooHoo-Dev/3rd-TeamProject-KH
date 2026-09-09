using Unity.Cinemachine;
using UnityEngine;

[RequireComponent(typeof(Camera))]
public sealed class FixedAspectViewport : MonoBehaviour
{
    [SerializeField] private float targetAspect = 16f / 9f;

    private Camera targetCamera;
    private int previousWidth;
    private int previousHeight;

    private void Awake()
    {
        targetCamera = GetComponent<Camera>();
        targetCamera.clearFlags = CameraClearFlags.SolidColor;
        targetCamera.backgroundColor = Color.black;
        DisableVirtualCameraRenderers();
        BindScreenSpaceCanvases();
        ApplyViewport();
    }

    private void Start()
    {
        // 비활성 프리팹이 활성화되거나 초기화 순서가 늦는 Canvas도 한 번 더 보정한다.
        BindScreenSpaceCanvases();
    }

    private void Update()
    {
        if (previousWidth == Screen.width && previousHeight == Screen.height) return;
        ApplyViewport();
    }

    private void ApplyViewport()
    {
        previousWidth = Screen.width;
        previousHeight = Screen.height;

        float screenAspect = (float)Screen.width / Screen.height;

        if (screenAspect > targetAspect)
        {
            float width = targetAspect / screenAspect;
            targetCamera.rect = new Rect((1f - width) * 0.5f, 0f, width, 1f);
        }
        else
        {
            float height = screenAspect / targetAspect;
            targetCamera.rect = new Rect(0f, (1f - height) * 0.5f, 1f, height);
        }
    }

    private void DisableVirtualCameraRenderers()
    {
        foreach (Camera camera in FindObjectsByType<Camera>(FindObjectsSortMode.None))
        {
            if (camera == targetCamera || camera.GetComponent<CinemachineCamera>() == null)
                continue;

            // Cinemachine Camera는 실제 화면을 렌더링하지 않고 Brain에 렌즈/위치만 제공한다.
            // Camera 컴포넌트가 켜져 있으면 Main Camera의 letterbox 결과를 덮어쓴다.
            camera.enabled = false;
        }
    }

    private void BindScreenSpaceCanvases()
    {
        foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera == null)
                canvas.worldCamera = targetCamera;

            if (canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera == targetCamera)
            {
                // 월드 스프라이트(카메라에서 약 10 단위 거리)보다 앞에 배치해야
                // 광물/지형의 깊이 버퍼가 UI를 가리지 않는다.
                canvas.planeDistance = targetCamera.nearClipPlane + 0.01f;
            }
        }
    }
}
