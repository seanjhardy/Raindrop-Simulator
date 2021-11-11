using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class Simulation : MonoBehaviour {
    public GameObject canvas;
    public RawImage image;
    public ComputeShader shader;

    private int width;
    private int height;
    private int step;
    public float delay = 0.5F;
    private float lastTime = 0f;

    private RenderTexture noise;

    private RenderTexture viewMap;
    private RenderTexture[] maps;
    private uint numTex = 2;
    private uint curTex = 0;
    private uint prevTex = 1;

    struct Raindrop {
        public Vector2 position;
        public Vector2 velocity;
        public float mass;
    };

    private ComputeBuffer computeBuffer;

    // Start is called before the first frame update
    void Start() {
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

        maps = new RenderTexture[2];
        maps[0] = map;
        maps[1] = map2;

        image.texture = noise;
    }


    // Update is called once per frame
    void Update() {
        if (Time.time - lastTime >= delay){
            lastTime = Time.time;
            step += 1;

            prevTex = curTex;
            curTex = (curTex + 1) % numTex;
            drawBlack();
            addRain();
            simulateStep();
            draw();
            resetPrevDataMap();

            image.texture = viewMap;
        }
    }

    void drawBlack() {
        int kernelHandle = shader.FindKernel("DrawBlack");

        shader.SetTexture(kernelHandle, "viewMap", viewMap);
        shader.Dispatch(kernelHandle, width / 16, height / 16, 1);
    }

    void draw() {
        int kernelHandle = shader.FindKernel("Draw");

        shader.SetTexture(kernelHandle, "viewMap", viewMap);
        shader.SetTexture(kernelHandle, "nextDataMap", maps[curTex]); 
        shader.Dispatch(kernelHandle, width / 16, height / 16, 1);
    }

    void resetPrevDataMap() {
        int kernelHandle = shader.FindKernel("ResetMap");

        shader.SetTexture(kernelHandle, "dataMap", maps[prevTex]);
        shader.Dispatch(kernelHandle, width / 16, height / 16, 1);
    }


    void addRain() {
        int kernelHandle = shader.FindKernel("AddRain");
        shader.SetFloat("deltaTime", Time.deltaTime);

        int numDrops = 1;
        Raindrop[] rain = new Raindrop[numDrops];

        float rainAngle = (float)(Mathf.PI * 0.5f + Mathf.Sin(step * 0.05f) / 8.0f);

        for (int i = 0; i < numDrops; i++) {
            Raindrop drop = new Raindrop();
            float x = Random.Range(1, width);
            float y = Random.Range(1, height);
            drop.position = new Vector2(x, y);

            float speed = 0.5f;
            float xVel = Mathf.Cos(rainAngle) * speed;
            float yVel = Mathf.Sin(rainAngle) * speed;

            drop.velocity = new Vector2(xVel, yVel);
            drop.mass = 1.0f;
            rain[i] = drop;
        }

        //set rain list
        computeBuffer = new ComputeBuffer(numDrops, sizeof(float) * 5);
        shader.SetTexture(kernelHandle, "dataMap", maps[prevTex]);
        computeBuffer.SetData(rain);
        shader.SetBuffer(kernelHandle, "rain", computeBuffer);
        shader.Dispatch(kernelHandle, numDrops, 1, 1);
        computeBuffer.Release();
    }

    void simulateStep() {
        int kernelHandle = shader.FindKernel("Update");
        shader.SetFloat("deltaTime", Time.deltaTime);

        shader.SetTexture(kernelHandle, "dataMap", maps[prevTex]);
        shader.SetTexture(kernelHandle, "nextDataMap", maps[curTex]);
        shader.SetTexture(kernelHandle, "viewMap", viewMap);
        shader.SetTexture(kernelHandle, "noiseMap", noise);

        shader.Dispatch(kernelHandle, width / 16, height / 16, 1);
    }

    void generateNoise() {
        float[] octaveFrequencies = new float[] { 0.01f, 0.05f, 0.2f, 2f, 5f };
        float[] octaveAmplitudes = new float[] { 2.0f,  1.0f, 0.6f, 0.4f, 0.2f };

        Texture2D noiseTex = new Texture2D(width, height);

        Color fillColor = new Color(0.0f, 0.0f, 0.0f, 1.0f);
        Color[] tex = noiseTex.GetPixels();
        float max = 0;
        float min = 10;
        for (var i = 0; i < tex.Length; ++i){
            tex[i] = fillColor;

            int x = i % width;
            int y = i / width;

            for (int j = 0; j < octaveFrequencies.Length; j++){
                float z = octaveAmplitudes[j] * Mathf.PerlinNoise(
                        octaveFrequencies[j] * x + .3f,
                        octaveFrequencies[j] * y + .3f) * 0.5f;
                float c = z + tex[i].r;

                tex[i] = new Color(c, c, c, 1.0f);
            }

            max = Mathf.Max(max, tex[i].r);
            min = Mathf.Min(min, tex[i].r);
        }

        for (var i = 0; i < tex.Length; ++i){
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
}
