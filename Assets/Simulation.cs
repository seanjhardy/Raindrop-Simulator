using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class Simulation : MonoBehaviour
{
    public GameObject canvas;
    public RawImage image;
    public Texture2D backgroundTexture;
    private RenderTexture bg, blurBg; 
    public ComputeShader shader;

    private int width;
    private int height;
    private int step;
    public float delay = 0.5F;
    private float lastTime = 0f;
    private float[] debugVals = new float[2];

    private RenderTexture noise;

    private RenderTexture viewMap;
    private RenderTexture[] maps;
    private uint numTex = 2;
    private uint curTex = 0;
    private uint prevTex = 1;

    struct Raindrop
    {
        public Vector2 position;
        public Vector2 velocity;
        public float mass;
    };

    private ComputeBuffer computeBuffer;

    // Start is called before the first frame update
    void Start()
    {
        width = 640;//640, 128
        height = 360;//360, 72

        generateNoise();

        shader.SetInt("width", width);
        shader.SetInt("height", height);

        RenderTexture map = new RenderTexture(width, height, 24);
        map.enableRandomWrite = true;
        map.Create();
        map.filterMode = FilterMode.Point;

        RenderTexture map2 = new RenderTexture(width, height, 24);
        map2.enableRandomWrite = true;
        map2.Create();
        map2.filterMode = FilterMode.Point;

        viewMap = new RenderTexture(width, height, 24);
        viewMap.enableRandomWrite = true;
        viewMap.Create();
        viewMap.filterMode = FilterMode.Point;
        drawBlack();

        maps = new RenderTexture[2];
        maps[0] = map;
        maps[1] = map2;

        initBg();
    }


    // Update is called once per frame
    void Update(){
        if (Time.time - lastTime >= delay){
            lastTime = Time.time;
            step += 1;

            prevTex = curTex;
            curTex = (curTex + 1) % numTex;
            addRain();
            simulateStep();
            draw();
            resetPrevDataMap();

            if (step % 10 == 0){
                Debug.Log(debugVals[0] + " " + debugVals[1]);
            }

            image.texture = viewMap;
        }
    }

    void drawBlack()
    {
        int kernelHandle = shader.FindKernel("DrawBlack");

        shader.SetTexture(kernelHandle, "viewMap", viewMap);
        shader.Dispatch(kernelHandle, width / 16, height / 16, 1);
    }

    void draw()
    {
        int kernelHandle = shader.FindKernel("Draw");

        shader.SetTexture(kernelHandle, "viewMap", viewMap);
        shader.SetTexture(kernelHandle, "nextDataMap", maps[curTex]);
        shader.SetTexture(kernelHandle, "noiseMap", noise);
        shader.SetTexture(kernelHandle, "bg", bg);
        shader.SetTexture(kernelHandle, "blurBg", blurBg);
        shader.Dispatch(kernelHandle, width / 16, height / 16, 1);
    }

    void resetPrevDataMap()
    {
        int kernelHandle = shader.FindKernel("ResetMap");

        shader.SetTexture(kernelHandle, "dataMap", maps[prevTex]);
        shader.Dispatch(kernelHandle, width / 16, height / 16, 1);
    }


    void addRain()
    {
        int kernelHandle = shader.FindKernel("AddRain");
        shader.SetFloat("deltaTime", Time.deltaTime);

        int numDrops = 1;
        int maxSize = 2;
        int dropSize = (int)Mathf.Pow(maxSize, 2);
        Raindrop[] rain = new Raindrop[numDrops*dropSize];

        float rainAngle = (float)(-Mathf.PI * 0.5f + Mathf.Sin(step * 0.005f) / 8.0f);

        for (int i = 0; i < numDrops; i++)
        {
            int size = Random.Range(1, maxSize + 1);
            float X = Random.Range(1, width);
            float Y = Random.Range(1, height);
            float speed = Random.Range(1, 10) * 0.1f;
            float xVel = Mathf.Cos(rainAngle) * speed;
            float yVel = Mathf.Sin(rainAngle) * speed;
            float mass = Random.Range(1, 20)*0.1f;

            for (int x = 0; x < size; x++) {
                for (int y = 0; y < size; y++) {
                    Raindrop drop = new Raindrop();
                    drop.position = new Vector2((float)x + X, (float)y + Y);
                    drop.velocity = new Vector2(xVel, yVel);
                    drop.mass = mass;
                    rain[i * dropSize + x* size + y] = drop;
                }
            }
        }

        //set rain list
        computeBuffer = new ComputeBuffer(numDrops * dropSize, sizeof(float) * 5);
        shader.SetTexture(kernelHandle, "dataMap", maps[prevTex]);
        computeBuffer.SetData(rain);
        shader.SetBuffer(kernelHandle, "rain", computeBuffer);
        shader.Dispatch(kernelHandle, numDrops * dropSize, 1, 1);
        computeBuffer.Release();
    }

    void simulateStep()
    {
        int kernelHandle = shader.FindKernel("Update");
        shader.SetFloat("deltaTime", Time.deltaTime);

        shader.SetInt("step", step);
        shader.SetTexture(kernelHandle, "dataMap", maps[prevTex]);
        shader.SetTexture(kernelHandle, "nextDataMap", maps[curTex]);
        shader.SetTexture(kernelHandle, "viewMap", viewMap);
        shader.SetTexture(kernelHandle, "noiseMap", noise);

        computeBuffer = new ComputeBuffer(debugVals.Length, sizeof(float));
        computeBuffer.SetData(debugVals);
        shader.SetBuffer(kernelHandle, "debugVals", computeBuffer);

        shader.Dispatch(kernelHandle, width / 16, height / 16, 1);
        computeBuffer.GetData(debugVals);
        computeBuffer.Release();
    }

    void generateNoise()
    {
        //float[] octaveFrequencies = new float[] { 0.01f, 0.05f, 0.2f, 2f, 5f };
        //float[] octaveAmplitudes = new float[] { 2.0f, 1.0f, 0.6f, 0.4f, 0.2f };
        float[] octaveFrequencies = new float[] { 0.2f, 2f, 5f, 8f };
        float[] octaveAmplitudes = new float[] { 0.1f, 0.2f, 0.2f, 0.2f };

        Texture2D noiseTex = new Texture2D(width, height);

        Color fillColor = new Color(0.0f, 0.0f, 0.0f, 1.0f);
        Color[] tex = noiseTex.GetPixels();
        float max = 0;
        float min = 10;
        for (var i = 0; i < tex.Length; ++i)
        {
            tex[i] = fillColor;

            int x = i % width;
            int y = i / width;

            for (int j = 0; j < octaveFrequencies.Length; j++)
            {
                float z = octaveAmplitudes[j] * Mathf.PerlinNoise(
                        octaveFrequencies[j] * x + .3f,
                        octaveFrequencies[j] * y + .3f) * 0.5f;
                float c = z + tex[i].r;

                tex[i] = new Color(c, c, c, 1.0f);
            }

            max = Mathf.Max(max, tex[i].r);
            min = Mathf.Min(min, tex[i].r);
        }

        for (var i = 0; i < tex.Length; ++i)
        {
            float c = (tex[i].r - min) * (1.0f / (max - min));
            tex[i] = new Color(c, c, c, 1.0f);
        }

        noiseTex.SetPixels(tex);

        noiseTex.Apply();

        noise = new RenderTexture(noiseTex.width, noiseTex.height, 24);
        noise.enableRandomWrite = true;
        noise.Create();
        noise.filterMode = FilterMode.Point;
        RenderTexture.active = noise;
        // Copy your texture ref to the render texture
        Graphics.Blit(noiseTex, noise);
    }


    public void initBg() {
        bg = new RenderTexture(width, height, 24);
        bg.enableRandomWrite = true;
        bg.Create();
        bg.filterMode = FilterMode.Point;
        RenderTexture.active = bg;

        Graphics.Blit(backgroundTexture, bg);

        blurBg = new RenderTexture(width, height, 24);
        blurBg.enableRandomWrite = true;
        blurBg.Create();
        blurBg.filterMode = FilterMode.Point;

        int kernelHandle = shader.FindKernel("Blur");

        shader.SetTexture(kernelHandle, "blurBg", blurBg);
        shader.SetTexture(kernelHandle, "bg", bg);

        shader.Dispatch(kernelHandle, width / 16, height / 16, 1);
    }
}