using Anaglyph.DisplayCapture;
using OVR.OpenVR;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class ServerSolution : MonoBehaviour
{
    private Texture2D textureToSend; // ������ Texture2D

    public RawImage objectDetectRenderer; // YOLO ����� ǥ���� ������
    public RawImage segmentationRenderer; // DeepLabV3 ����� ǥ���� ������

    int frameStack = 0;

    public void SendToServer()
    {
        if (frameStack < 30) 
        {
            frameStack++;
        } else
        {
            textureToSend = DisplayCaptureManager.Instance.ScreenCaptureTexture;
            StartCoroutine(UploadImage("http://192.168.0.78:5000/process", textureToSend));
            frameStack = 0;
        }
    }

    IEnumerator UploadImage(string url, Texture2D textureToSend)
    {
        // Texture2D�� ����Ʈ �迭�� ��ȯ (PNG ����)
        byte[] imageBytes = textureToSend.EncodeToPNG();

        // ��û ����
        UnityWebRequest request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(imageBytes);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/octet-stream"); // ���̳ʸ� ������ ����

        // ��û ����
        yield return request.SendWebRequest();

        // ���� ó��
        if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
        {
            Debug.LogError($"Error: {request.error}");
        }
        else
        {
            // JSON ���� ó��
            string jsonResponse = request.downloadHandler.text;
            ProcessServerResponse(jsonResponse, textureToSend);
        }
    }

    void ProcessServerResponse(string jsonResponse, Texture2D textureToSend)
    {
        // JSON �Ľ�
        var responseData = JsonUtility.FromJson<ServerResponse>(jsonResponse);
            
        // YOLO ����� �ð�ȭ
        if (responseData.yolo_results != null)
        {
            DisplayYoloResults(responseData.yolo_results, textureToSend);
        }

        // DeepLabV3 ����� �ð�ȭ
        if (responseData.deeplab_segmentation != null)
        {
            DisplaySegmentationResults(responseData.deeplab_segmentation, textureToSend);
        }
    }

    void DisplayYoloResults(List<YoloBox> yoloResults, Texture2D textureToSend)
    {
        Texture2D yoloTexture = Instantiate(textureToSend); // ���� �̹��� ����

        foreach (var box in yoloResults)
        {
            DrawBoundingBox(yoloTexture, box.x, box.y, box.width, box.height);
        }

        // YOLO ����� �������� ����
        objectDetectRenderer.texture = yoloTexture;
    }

    void DisplaySegmentationResults(List<List<int>> segmentationData, Texture2D textureToSend)
    {
        int width = segmentationData[0].Count;
        int height = segmentationData.Count;

        Texture2D segmentationTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[width * height];

        // ���׸����̼� �����͸� ���� ������ ��ȯ
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int classId = segmentationData[y][x];
                pixels[y * width + x] = ClassIdToColor(classId);
            }
        }
        segmentationTexture.SetPixels(pixels);
        segmentationTexture.Apply();
        // ���׸����̼� ����� �������� ����
        segmentationRenderer.texture = segmentationTexture;
    }

    void DrawBoundingBox(Texture2D texture, float x, float y, float width, float height)
    {
        Color color = Color.red; // �ٿ�� �ڽ� ����

        for (int i = 0; i < width; i++)
        {
            texture.SetPixel((int)x + i, (int)y, color); // ���
            texture.SetPixel((int)x + i, (int)(y + height), color); // �ϴ�
        }

        for (int i = 0; i < height; i++)
        {
            texture.SetPixel((int)x, (int)y + i, color); // ����
            texture.SetPixel((int)(x + width), (int)y + i, color); // ����
        }

        texture.Apply();
    }

    Color ClassIdToColor(int classId)
    {
        // Ŭ���� ID�� �������� ��ȯ (�ܼ� ����)
        switch (classId)
        {
            case 0: return Color.red;
            case 1: return Color.green;
            case 2: return Color.blue;
            default: return Color.black;
        }
    }

    // JSON ���信 �´� Ŭ���� ����
    [System.Serializable]
    public class ServerResponse
    {
        public List<YoloBox> yolo_results;
        public List<List<int>> deeplab_segmentation;
    }

    [System.Serializable]
    public class YoloBox
    {
        public float x;
        public float y;
        public float width;
        public float height;
        public int classId;
        public float confidence;
    }
}
