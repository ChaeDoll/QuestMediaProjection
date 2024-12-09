using Anaglyph.DisplayCapture;
using System.Linq;
using TMPro;
using Unity.Sentis;
using UnityEngine;
using UnityEngine.UI;

public class SegmentationManager : MonoBehaviour
{
    // public TMP_Text debugText;
    public RawImage displayPannel;
    public RawImage originImage;

    [Header("Initial Setting")]
    [SerializeField] private ModelAsset modelAsset;
    [SerializeField] private float updateInterval;
    [SerializeField] private float segmentThreshold;

    private float lastUpdateTime = 0f;
    private DisplayCaptureManager displayCaptureManager;
    private Model model;
    private IWorker engine;
    private RenderTexture targetRT;

    [SerializeField] private GameObject DetectDisplay;
    [SerializeField] private GameObject SegmentDisplay;

    private Color[] classColors = new Color[]
    {
        new Color(0f, 0f, 0f, 0f),       // Class 0 - �����
        new Color(255f / 255f, 255f / 255f, 0f / 255f, 0.4f),   // Class 1 - ��� (����)
        new Color(255f / 255f, 255f / 255f, 0f / 255f, 0.4f),   // Class 2 - ��� (���ں��)
        new Color(255f / 255f, 0f / 255f, 0f / 255f, 0.4f),      // Class 3 - ���� (����) 
        new Color(255f / 255f, 0f / 255f, 0f / 255f, 0.4f),      // Class 4 - ���� (����)
        new Color(135f / 255f, 206f / 255f, 235f / 255f, 0.4f), // Class 5 - �ϴû� (Ⱦ�ܺ���)
        new Color(255f / 255f, 0f / 255f, 0f / 255f, 0.4f),   // Class 6 - ������ (�����ŵ���)
        new Color(255f / 255f, 0f / 255f, 0f / 255f, 0.4f)      // Class 7 - ������ (���豸��)
    };

    private const int imageWidth = 256;
    private const int imageHeight = 256;

    void Awake()
    {
        targetRT = new RenderTexture(imageWidth, imageHeight, 0);
        displayCaptureManager = DisplayCaptureManager.Instance;
    }

    private void OnEnable()
    {
        displayPannel.enabled = true;

        model = ModelLoader.Load(modelAsset);
        engine = WorkerFactory.CreateWorker(BackendType.GPUCompute, model);
        displayCaptureManager.onNewFrame.AddListener(ImageSegmentation);
    }

    private void OnDisable()
    {
        displayPannel.enabled = false;

        model = null;
        engine?.Dispose();
        displayCaptureManager.onNewFrame?.RemoveListener(ImageSegmentation);
    }


    public void ImageSegmentation()
    {
        if (Time.time - lastUpdateTime < updateInterval) return;
        lastUpdateTime = Time.time;

        //originImage.texture = displayCaptureManager.ScreenCaptureTexture;

        DetectDisplay.SetActive(false);
        SegmentDisplay.SetActive(false);

        Graphics.Blit(displayCaptureManager.ScreenCaptureTexture, targetRT);
        using TensorFloat inputTensor = TextureConverter.ToTensor(targetRT, imageWidth, imageHeight, 3);
        inputTensor.CompleteOperationsAndDownload();

        DetectDisplay.SetActive(true);
        SegmentDisplay.SetActive(true);

        NormalizeTensor(inputTensor, imageHeight, imageWidth);

        engine.Execute(inputTensor);
        var outputTensor = engine.PeekOutput() as TensorFloat;
        outputTensor.CompleteOperationsAndDownload();

        ApplySoftmax(outputTensor, 8, imageHeight, imageWidth);

        // 1. �ټ����� Ŭ���� �� ����
        int[,] classMap = GenerateClassMap(outputTensor, imageWidth, imageHeight);
        // 2. Ŭ���� �ʿ��� Texture2D ����
        Texture2D segmentationTexture = GenerateSegmentationTexture(classMap, imageWidth, imageHeight);
        // 3. RawImage�� �ؽ�ó ���
        displayPannel.texture = segmentationTexture;

        outputTensor.Dispose();
    }

    private void NormalizeTensor(TensorFloat inputTensor, int height, int width)
    {
        // ImageNet ��� �� ǥ������ �� (RGB ����)
        float[] mean = { 0.485f, 0.456f, 0.406f };
        float[] std = { 0.229f, 0.224f, 0.225f };

        for (int c = 0; c < mean.Length; c++) // RGB ä��
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float value = inputTensor[0, c, y, x];
                    value = (value - mean[c]) / std[c]; // ����ȭ
                    inputTensor[0, c, y, x] = value; // ����ȭ �� �ٽ� ����
                }
            }
        }
    }

    private int[,] GenerateClassMap(TensorFloat outputs, int width, int height)
    {
        float[] outputData = outputs.ToReadOnlyArray(); // Sentis �ټ��� float �迭�� ��ȯ
        int numClasses = outputs.shape[1]; // Ŭ���� ��
        int[,] classMap = new int[height, width];
        // Loop through pixels
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int pixelIndex = y * width + x;
                float maxScore = float.MinValue;
                int maxClass = 0;

                // Ŭ������ ���� �� (argmax)
                for (int c = 0; c < numClasses; c++)
                {
                    float score = outputData[c * width * height + pixelIndex];
                    if (score > maxScore)
                    {
                        maxScore = score;
                        maxClass = c;
                    }
                }
                if (maxScore > segmentThreshold) 
                { classMap[y, x] = maxClass; }
                else
                { classMap[y, x] = 0; }
            }
        }
        return classMap;
    }

    // Ŭ���� �ʿ��� Texture2D ����
    private Texture2D GenerateSegmentationTexture(int[,] classMap, int width, int height)
    {
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                int classId = classMap[y, x];
                pixels[index] = classColors[classId];
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    private void ApplySoftmax(TensorFloat inputTensor, int numClasses, int height, int width)
    {
        float[] logits = new float[numClasses];
        for (int y=0; y < height; y++)
        {
            for (int x=0; x < width; x++)
            {
                // 1. �� �ȼ��� Logits �� ��������
                for (int c=0; c < numClasses; c++)
                {
                    int index = (c * height + y) * width + x;
                    logits[c] = inputTensor[index];
                }

                // 2. Softmax ���
                float[] probabilities = Softmax(logits);

                for (int c=0; c < numClasses ; c++)
                {
                    int index = (c * height + y) * width + x;
                    inputTensor[index] = probabilities[c];
                }
            }
        }
    }

    private float[] Softmax(float[] logits)
    {
        float maxLogit = Mathf.Max(logits);
        float[] exps = new float[logits.Length];
        float sumExps = 0f;
        for (int i = 0; i < logits.Length; i++)
        {
            exps[i] = Mathf.Exp(logits[i] - maxLogit);
            sumExps += exps[i];
        }
        for (int i = 0; i < logits.Length; i++)
        {
            exps[i] /= sumExps;
        }
        return exps;
    }
}
