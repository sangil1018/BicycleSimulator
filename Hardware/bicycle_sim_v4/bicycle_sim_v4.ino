// ================================================================
//  자전거 안전체험 시뮬레이터  ESP32-S3 펌웨어 v4.1
//  빛고을국민안전체험관 / FLUXION / 2026.05
//  v4.1: O·X 버튼 LED 제거
// ================================================================
//
//  [핀 배정]
//  GPIO1   Right Row4   PAS 센서 신호 (파란선, INPUT_PULLUP)
//  GPIO2   Right Row5   진동 모터 L (PWM)
//  GPIO42  Right Row6   진동 모터 R (PWM)
//  GPIO4   Left  Row4   브레이크 좌 (INPUT_PULLUP)
//  GPIO5   Left  Row5   브레이크 우 (INPUT_PULLUP)
//  GPIO6   Left  Row6   O 버튼 (INPUT_PULLUP)
//  GPIO7   Left  Row7   X 버튼 (INPUT_PULLUP)
//  GPIO11  Left  Row17  ICM-20948 SDA
//  GPIO12  Left  Row18  ICM-20948 SCL
//  GPIO48  내장          상태 RGB LED (WS2812)
//  3V3     Left  Row1   PAS + ICM-20948 + 진동모듈 VCC
//  GND     Left  Row22  공통 접지
//  USB-C   하단          PC 연결 (시리얼 + 전원)
//
//  [PAS 센서]  빨간→3V3  노란→GND  파란→GPIO1
//  [진동 모듈] VCC→3V3  GND→GND  IN_L→GPIO2  IN_R→GPIO42
//
//  [시리얼 ESP32→Unity] 115200bps / 50Hz / JSON
//  {"id":1,"rpm":80.0,"spd":20.0,"str":-5.2,"brkL":0,"brkR":0,"o":0,"x":0}
//
//  [시리얼 Unity→ESP32]
//  V0~5 진동 / C 조향캘리브레이션 / M 지자기캘리브레이션(5초 회전) / S0~3 RGB 상태
// ================================================================

#include <Wire.h>
#include <ICM_20948.h>
#include "vibe_types.h"

ICM_20948_I2C icm;

#define USE_RGB_LED 1
#if USE_RGB_LED
#include <Adafruit_NeoPixel.h>
Adafruit_NeoPixel rgb(1, 48, NEO_GRB + NEO_KHZ800);
void setRGB(uint8_t r, uint8_t g, uint8_t b)
{
    rgb.setPixelColor(0, rgb.Color(r, g, b));
    rgb.show();
}
#else
void setRGB(uint8_t, uint8_t, uint8_t) {}
#endif

// ── 핀 정의 ─────────────────────────────────────────────────────
#define PIN_PAS 1     // GPIO1   Right Row4  PAS 센서 (파란선)
#define PIN_VIBE_L 2  // GPIO2   Right Row5  진동 모터 L
#define PIN_VIBE_R 42 // GPIO42  Right Row6  진동 모터 R
#define PIN_BRK_L 4   // GPIO4   Left  Row4  브레이크 좌
#define PIN_BRK_R 5   // GPIO5   Left  Row5  브레이크 우
#define PIN_BTN_O 6   // GPIO6   Left  Row6  O 버튼
#define PIN_BTN_X 7   // GPIO7   Left  Row7   X 버튼
#define PIN_SDA 17    // GPIO17  Left  Row17  ICM-20948 SDA (11에서 변경)
#define PIN_SCL 18    // GPIO18  Left  Row18  ICM-20948 SCL (12에서 변경)
// GPIO11, GPIO12는 ESP32-S3 내부 플래시/PSRAM용으로 사용되므로 피해야 함

// ── LEDC PWM (진동) ──────────────────────────────────────────────
#define VIBE_FREQ 1000
#define VIBE_BITS 8

// ── 시스템 상수 ─────────────────────────────────────────────────
#define STATION_ID 1
#define PAS_MAGNETS 12
#define RPM_MAX 300.0f       // F1: 스파이크 클램프 상한
#define CADENCE_TO_KPH 0.25f // 60 RPM = 15 km/h
#define SERIAL_HZ 50
#define FILTER_HZ 102 // 1125 / (1 + SMPLRT_DIV=10) = 102.27Hz
#define STEER_RANGE_DEG 90.0f
#define PAS_TIMEOUT 500000UL // 0.5초 (페달 정지 후 속도 0 전환 시간)
#define GYRO_SCALE 1.0f
// F9: 배열 길이 자동 계산
#define ARRAY_LEN(a) (int)(sizeof(a) / sizeof((a)[0]))

// ── 진동 패턴 ────────────────────────────────────────────────────

const VibeStep PAT_DANGER[] = {{255, 700}, {0, 0}};
const VibeStep PAT_SUCCESS[] = {{220, 150}, {0, 80}, {220, 150}, {0, 0}};
const VibeStep PAT_CORRECT[] = {{180, 100}, {0, 0}};
const VibeStep PAT_WRONG[] = {{255, 450}, {0, 0}};
const VibeStep PAT_WALK[] = {{200, 100}, {0, 80}, {200, 100}, {0, 80}, {200, 100}, {0, 0}};
const VibeStep PAT_READY[] = {{160, 80}, {0, 60}, {200, 120}, {0, 0}};

static VibeStep g_vSeq[10];
static int g_vCount = 0, g_vIdx = 0;
static uint32_t g_vEnd = 0;

// ── 전역 변수 ────────────────────────────────────────────────────
volatile uint32_t g_lastPulseUs = 0;
volatile uint32_t g_pulseIntervalUs = UINT32_MAX;
float g_steerAngle = 0.0f;  // 통합 조향각 (도)
float g_steerBias  = 0.0f;  // 조향축 자이로 바이어스 (dps)
float g_gyroBiasX  = 0.0f;  // X축 자이로 초기 바이어스
float g_gyroBiasY  = 0.0f;  // Y축 자이로 초기 바이어스
float g_gyroBiasZ  = 0.0f;  // Z축 자이로 초기 바이어스
float g_gravX      = 0.0f;  // 중력 방향 (정규화, setup에서 측정)
float g_gravY      = 0.0f;
float g_gravZ      = 1.0f;
float g_magOffX = 0.0f;
float g_magOffY = 0.0f;
float g_magOffZ = 0.0f;
uint32_t g_lastSteerMicros = 0; // 조향 적분용 정밀 시간 변수
String g_rxBuf = "";
uint32_t g_lastSendMs = 0;

// ── 인터럽트 ─────────────────────────────────────────────────────
void IRAM_ATTR onPasPulse()
{
    uint32_t now = micros();
    g_pulseIntervalUs = now - g_lastPulseUs;
    g_lastPulseUs = now;
}

// ── 진동 ─────────────────────────────────────────────────────────
void vibeSet(uint8_t s)
{
    ledcWrite(PIN_VIBE_L, s);
    ledcWrite(PIN_VIBE_R, s);
}
bool vibeIsPlaying() { return g_vIdx < g_vCount; }
void vibeStop()
{
    g_vCount = g_vIdx = 0;
    vibeSet(0);
}

void playVibe(const VibeStep *p, int n)
{
    if (n <= 0 || n > 10)
        return;
    memcpy(g_vSeq, p, (size_t)n * sizeof(VibeStep));
    g_vCount = n;
    g_vIdx = 0;
    g_vEnd = millis() + p[0].ms;
    vibeSet(p[0].s);
}

void updateVibe()
{
    if (g_vIdx >= g_vCount)
        return;
    if ((uint32_t)(millis() - g_vEnd) >= 0x80000000UL)
        return; // F7: wrap-safe
    if (++g_vIdx >= g_vCount)
    {
        vibeSet(0);
        return;
    }
    vibeSet(g_vSeq[g_vIdx].s);
    g_vEnd = millis() + g_vSeq[g_vIdx].ms;
}

// ── ICM-20948 조향 (중력축 자이로 적분) ──────────────────────────
// Mahony + 지자기 방식 대신 중력축 투영 적분 사용.
// 실내 자기장 간섭으로 Mahony yaw가 능동적으로 틀린 방향으로 수렴하는 문제 해결.
float readSteering()
{
    if (!icm.dataReady())
        return constrain(g_steerAngle, -STEER_RANGE_DEG, STEER_RANGE_DEG);

    // 데이터가 준비되었을 때만 실제 시간 간격(dt) 계산
    uint32_t nowUs = micros();
    if (g_lastSteerMicros == 0) g_lastSteerMicros = nowUs;
    float dt = (float)(nowUs - g_lastSteerMicros) / 1000000.0f;
    g_lastSteerMicros = nowUs;

    icm.getAGMT();
    // 초기 바이어스 제거 (setup에서 측정된 값)
    float gx = icm.gyrX() - g_gyroBiasX;
    float gy = icm.gyrY() - g_gyroBiasY;
    float gz = icm.gyrZ() - g_gyroBiasZ;
    float ax = icm.accX(), ay = icm.accY(), az = icm.accZ();

    // 조향 각속도 = 자이로 벡터를 중력축(수직축)에 투영
    float steerRate = gx * g_gravX + gy * g_gravY + gz * g_gravZ;

    // 정지 감지 및 동적 바이어스 업데이트: 
    // 보정된 데이터 기준 전 축 각속도 합산 노이즈가 매우 낮을 때만 바이어스 추정
    float accNorm = sqrtf(ax * ax + ay * ay + az * az);
    float gyrNorm = fabsf(gx) + fabsf(gy) + fabsf(gz); 
    if ((fabsf(accNorm - 1000.0f) < 80.0f) && (gyrNorm < 1.2f))
    {
        // IIR 바이어스 추정 (시정수 약 5초)
        g_steerBias += (steerRate - g_steerBias) * 0.002f;
    }

    // 최종 각속도 = 투영된 각속도 - 동적 바이어스
    float rate = steerRate - g_steerBias;

    // 강력한 데드존 필터 상향 (0.5 -> 1.2 dps)
    // 가만히 있을 때 값이 흐르는 현상을 방지하기 위해 임계값을 높입니다.
    if (fabsf(rate) < 1.2f) rate = 0.0f;

    // 적분: (도/초) * 초 = 도
    if (dt > 0.0f && dt < 0.1f) { 
        g_steerAngle += rate * dt;
    }

    return constrain(g_steerAngle, -STEER_RANGE_DEG, STEER_RANGE_DEG);
}

void calibrateGyro()
{
    Serial.println("{\"debug\":\"Calibrating Gyro... Do not move.\"}");
    setRGB(0, 0, 255); // 파란색: 자이로 보정 중
    float sumX = 0, sumY = 0, sumZ = 0;
    int n = 0;
    uint32_t t0 = millis();
    // 5초 동안 정밀 캘리브레이션 (시간 연장)
    while (millis() - t0 < 5000)
    {
        if (icm.dataReady())
        {
            icm.getAGMT();
            sumX += icm.gyrX();
            sumY += icm.gyrY();
            sumZ += icm.gyrZ();
            n++;
        }
        delay(5);
    }
    if (n > 0)
    {
        g_gyroBiasX = sumX / n;
        g_gyroBiasY = sumY / n;
        g_gyroBiasZ = sumZ / n;
    }
    g_steerBias = 0.0f; // 초기화
    Serial.printf("{\"debug\":\"Gyro Calibrated\",\"bias\":[%.3f,%.3f,%.3f]}\n",
                  g_gyroBiasX, g_gyroBiasY, g_gyroBiasZ);
    setRGB(0, 200, 0); // 초록색: 완료
}

void calibrateSteering()
{
    g_steerAngle = 0.0f;
    // 바이어스 추정값은 유지 (재수렴 시간 절약)
    Serial.printf("{\"calibrated\":true,\"center\":0.0}\n");
}

void calibrateMag()
{
    vibeStop();        // F5: 블로킹 전 모터 즉시 OFF
    setRGB(0, 0, 200); // F5: 파란색 = 지자기 캘리브레이션 중
    // 하드 아이언 캘리브레이션: 5초 동안 핸들을 좌우로 천천히 회전
    const uint32_t DUR = 5000;
    float xMn = 1e6f, xMx = -1e6f;
    float yMn = 1e6f, yMx = -1e6f;
    float zMn = 1e6f, zMx = -1e6f;
    Serial.println("{\"magcal\":\"start\",\"dur\":5}");
    uint32_t t0 = millis();
    while (millis() - t0 < DUR)
    {
        if (icm.dataReady())
        {
            icm.getAGMT();
            float x = icm.magX(), y = icm.magY(), z = icm.magZ();
            if (x < xMn)
                xMn = x;
            if (x > xMx)
                xMx = x;
            if (y < yMn)
                yMn = y;
            if (y > yMx)
                yMx = y;
            if (z < zMn)
                zMn = z;
            if (z > zMx)
                zMx = z;
        }
        delay(10);
    }
    g_magOffX = (xMx + xMn) * 0.5f;
    g_magOffY = (yMx + yMn) * 0.5f;
    g_magOffZ = (zMx + zMn) * 0.5f;
    Serial.printf("{\"magcal\":\"done\",\"ox\":%.2f,\"oy\":%.2f,\"oz\":%.2f}\n",
                  g_magOffX, g_magOffY, g_magOffZ);
    setRGB(0, 200, 0); // F5: 복구
}

// ── 속도 ─────────────────────────────────────────────────────────
float calcCadenceRPM()
{
    // F1: ISR과 공유하는 두 변수를 인터럽트 비활성화로 원자적 복사
    portDISABLE_INTERRUPTS();
    uint32_t interval = g_pulseIntervalUs;
    uint32_t last = g_lastPulseUs;
    portENABLE_INTERRUPTS();
    if (micros() - last > PAS_TIMEOUT || interval == 0)
        return 0.0f;
    float rpm = 60.0f / ((float)interval / 1e6f * (float)PAS_MAGNETS);
    return constrain(rpm, 0.0f, RPM_MAX); // F1: 스파이크 상한 클램프
}

// ── RGB 상태 ─────────────────────────────────────────────────────
enum RgbState
{
    RGB_IDLE,
    RGB_RUNNING,
    RGB_EVENT,
    RGB_QUIZ
};
RgbState g_rgbState = RGB_IDLE;

void updateRGB(float spd, bool brakeAny)
{
    static uint32_t blinkMs = 0;
    static bool blinkOn = false;
    uint32_t now = millis();
    if (now - blinkMs < 400)
        return;
    blinkMs = now;
    blinkOn = !blinkOn;
    if (brakeAny)
    {
        setRGB(255, 80, 0);
        return;
    }
    if (g_rgbState == RGB_EVENT)
    {
        setRGB(255, 0, 0);
        return;
    }
    if (g_rgbState == RGB_QUIZ)
    {
        setRGB(blinkOn ? 100 : 0, 0, blinkOn ? 100 : 0);
        return;
    }
    if (spd > 1.0f)
    {
        setRGB(0, 200, 0);
        return;
    }
    setRGB(blinkOn ? 40 : 0, blinkOn ? 40 : 0, blinkOn ? 40 : 0);
}

// ── 시리얼 수신 ──────────────────────────────────────────────────
void handleCommand(const String &cmd)
{
    if (cmd.length() < 1)
        return;
    char t = cmd.charAt(0);
    int v = (cmd.length() > 1) ? cmd.substring(1).toInt() : 0;
    switch (t)
    {
    case 'V':
        switch (v)
        {
        case 0:
            vibeStop();
            break;
        case 1:
            playVibe(PAT_DANGER, ARRAY_LEN(PAT_DANGER));
            break;
        case 2:
            playVibe(PAT_SUCCESS, ARRAY_LEN(PAT_SUCCESS));
            break;
        case 3:
            playVibe(PAT_CORRECT, ARRAY_LEN(PAT_CORRECT));
            break;
        case 4:
            playVibe(PAT_WRONG, ARRAY_LEN(PAT_WRONG));
            break;
        case 5:
            playVibe(PAT_WALK, ARRAY_LEN(PAT_WALK));
            break;
        }
        break;
    case 'C':
        calibrateSteering();
        break;
    case 'M':
        calibrateMag();
        break;
    case 'S':
        g_rgbState = (RgbState)constrain(v, 0, 3);
        break;
    }
}

void processSerial()
{
    while (Serial.available())
    {
        char c = (char)Serial.read();
        if (c == '\n' || c == '\r')
        {
            g_rxBuf.trim();
            if (g_rxBuf.length() > 0)
                handleCommand(g_rxBuf);
            g_rxBuf = "";
        }
        else if (g_rxBuf.length() < 16)
        {
            g_rxBuf += c;
        }
    }
}

// ── JSON 전송 ─────────────────────────────────────────────────────
void sendJSON(float rpm, float spd, float str,
              bool bL, bool bR, bool o, bool x)
{
    Serial.printf(
        "{\"id\":%d,\"rpm\":%.1f,\"spd\":%.1f,\"str\":%.1f,"
        "\"brkL\":%d,\"brkR\":%d,\"o\":%d,\"x\":%d}\n",
        STATION_ID, rpm, spd, str,
        bL ? 1 : 0, bR ? 1 : 0, o ? 1 : 0, x ? 1 : 0);
}

// ── setup ─────────────────────────────────────────────────────────
void setup()
{
    Serial.begin(115200);
    delay(1000); // 시리얼 모니터 대기

    // F11: I2C 초기화 (100kHz - ESP32-S3는 10kHz 이하에서 내부 타이밍 불안정)
    Wire.begin(PIN_SDA, PIN_SCL);
    Wire.setClock(100000);
    Wire.setTimeOut(500);
    delay(2000); // ICM-20948 전원 안정화 (datasheet: 최소 100ms, 여유 확보)

#if USE_RGB_LED
    rgb.begin();
    rgb.setBrightness(40);
    setRGB(40, 40, 40);
#endif

    pinMode(PIN_PAS, INPUT_PULLUP);
    attachInterrupt(digitalPinToInterrupt(PIN_PAS), onPasPulse, FALLING);
    pinMode(PIN_BRK_L, INPUT_PULLUP);
    pinMode(PIN_BRK_R, INPUT_PULLUP);
    pinMode(PIN_BTN_O, INPUT_PULLUP);
    pinMode(PIN_BTN_X, INPUT_PULLUP);

    ledcAttach(PIN_VIBE_L, VIBE_FREQ, VIBE_BITS);
    ledcAttach(PIN_VIBE_R, VIBE_FREQ, VIBE_BITS);
    vibeSet(0);

    // F12: 주소 자동 감지 (0x68 우선 시도)
    uint8_t addr = 0x69;
    Wire.beginTransmission(0x68);
    if (Wire.endTransmission() == 0)
        addr = 0x68;

    Serial.printf("{\"debug\":\"Detected Address\",\"addr\":\"0x%02X\"}\n", addr);

    // F3: 5회 재시도 (수동 pre-init 제거 - 라이브러리 자체 리셋 시퀀스와 충돌함)
    bool initialized = false;
    for (int i = 0; i < 5; i++)
    {
        // WHO_AM_I 수동 확인 (디버깅용)
        Wire.beginTransmission(addr);
        Wire.write(0x00);
        Wire.endTransmission(false);
        Wire.requestFrom(addr, (uint8_t)1);
        uint8_t id = Wire.available() ? Wire.read() : 0;

        ICM_20948_Status_e status = icm.begin(Wire, (addr == 0x69));
        Serial.printf("{\"retry\":%d,\"read_id\":%d,\"status\":%d}\n", i + 1, id, (int)status);

        if (status == ICM_20948_Stat_Ok)
        {
            initialized = true;
            break;
        }
        delay(1000);
    }

    if (!initialized)
    {
        Serial.println("{\"error\":\"CRITICAL: Hardware handshake failed. Check AD0 and Pull-up resistors.\"}");
        setRGB(255, 0, 0);
        while (true)
            delay(1000);
    }

    // 성공 시 통신 속도 정상화
    Wire.setClock(400000);

    // startupDefault()는 DLPF를 off 상태로 종료 → 자이로 ODR = 9kHz
    // DLPF 활성화 후 SMPLRT_DIV=10 → ODR = 1125/(1+10) = 102Hz
    icm.enableDLPF(ICM_20948_Internal_Gyr, true);
    icm.enableDLPF(ICM_20948_Internal_Acc, true);
    ICM_20948_smplrt_t smplrt;
    smplrt.g = 10;
    smplrt.a = 10;
    icm.setSampleRate((ICM_20948_Internal_Gyr | ICM_20948_Internal_Acc), smplrt);

    // 조향축(중력 방향) 측정: 1초 동안 가속도계 평균 → 정규화
    {
        float sumX = 0, sumY = 0, sumZ = 0;
        int n = 0;
        uint32_t t0 = millis();
        while (millis() - t0 < 1000)
        {
            if (icm.dataReady())
            {
                icm.getAGMT();
                sumX += icm.accX(); sumY += icm.accY(); sumZ += icm.accZ();
                n++;
            }
            delay(5);
        }
        if (n > 0)
        {
            float norm = sqrtf((sumX/n)*(sumX/n) + (sumY/n)*(sumY/n) + (sumZ/n)*(sumZ/n));
            if (norm > 0.1f)
            {
                g_gravX = (sumX/n) / norm;
                g_gravY = (sumY/n) / norm;
                g_gravZ = (sumZ/n) / norm;
            }
        }
        Serial.printf("{\"gravAxis\":[%.3f,%.3f,%.3f]}\n", g_gravX, g_gravY, g_gravZ);
    }
    calibrateGyro();
    calibrateSteering();
    playVibe(PAT_READY, ARRAY_LEN(PAT_READY));
    setRGB(0, 200, 0);
}

// ── loop ──────────────────────────────────────────────────────────
void loop()
{
    uint32_t now = millis();
    updateVibe();
    processSerial();

    // F10: 센서 데이터와 필터 업데이트는 매 루프마다 수행하여 조향 정밀도 향상
    // readSteering() 내부의 icm.dataReady()에 의해 실제 센서 속도에 맞춰 업데이트됨
    float str = readSteering();

    if (now - g_lastSendMs < (1000u / SERIAL_HZ))
        return;
    g_lastSendMs = now;

    float rpm = calcCadenceRPM();
    float spd = rpm * CADENCE_TO_KPH;
    bool brkL = !digitalRead(PIN_BRK_L);
    bool brkR = !digitalRead(PIN_BRK_R);
    bool btnO = !digitalRead(PIN_BTN_O);
    bool btnX = !digitalRead(PIN_BTN_X);

    if (!vibeIsPlaying())
        vibeSet((brkL || brkR) ? 180 : 0);
    updateRGB(spd, brkL || brkR);
    sendJSON(rpm, spd, str, brkL, brkR, btnO, btnX);
}
