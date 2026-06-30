// ================================================================
//  자전거 안전체험 시뮬레이터  ESP32-S3 펌웨어 v5.8
//  빛고을국민안전체험관 / FLUXION / 2026.06
//  v5.8: 진동 제어 ESP32 통합 (IRF520 모듈, GPIO2 PWM)
// ================================================================

#include <Wire.h>
#include <ICM_20948.h>
#include "vibe_types.h"

// [중요] DMP 사용을 위해 라이브러리 설정(ICM_20948_C.h)에서
// #define ICM_20948_USE_DMP 주석이 해제되어 있어야 합니다.

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
#define PIN_PAS    1
#define PIN_BRK    4
#define PIN_BTN_O  6
#define PIN_BTN_X  7
#define PIN_SDA   17
#define PIN_SCL   18
#define PIN_MOTOR  2   // IRF520 모듈 SIG

// ── 시스템 상수 ─────────────────────────────────────────────────
#define STATION_ID       1
#define PAS_MAGNETS     12
#define RPM_MAX        300.0f
#define CADENCE_TO_KPH   0.25f
#define SERIAL_HZ       50
#define STEER_RANGE_DEG 45.0f
#define PAS_TIMEOUT     500000UL

// ── 전역 변수 ────────────────────────────────────────────────────
volatile uint32_t g_lastPulseUs    = 0;
volatile uint32_t g_pulseIntervalUs = UINT32_MAX;
float    g_steerAngle  = 0.0f;
float    g_lastRawYaw  = 0.0f;
float    g_yawOffset   = 0.0f;
String   g_rxBuf       = "";
uint32_t g_lastSendMs  = 0;
bool     g_dmpStable   = false; 
uint32_t g_bootMs      = 0;

// ── 진동 시퀀서 ──────────────────────────────────────────────────
// 3.3V 게이트 환경 최적화: PWM 최대(255), 최소 ON 300ms (모터 스핀업 보장)
const VibeStep PAT_DANGER[]  = {{255, 900}, {0, 0}};
const VibeStep PAT_SUCCESS[] = {{255, 400}, {0, 100}, {255, 400}, {0, 0}};
const VibeStep PAT_CORRECT[] = {{255, 500}, {0, 0}};
const VibeStep PAT_WRONG[]   = {{255, 650}, {0, 0}};
const VibeStep PAT_WALK[]    = {{255, 400}, {0, 100}, {255, 400}, {0, 100}, {255, 400}, {0, 0}};
const VibeStep PAT_READY[]   = {{255, 350}, {0, 80},  {255, 500}, {0, 0}};

static VibeStep  g_vSeq[10];
static int       g_vCount = 0, g_vIdx = 0;
static uint32_t  g_vEnd   = 0;
static bool      g_brake  = false;
static uint16_t  g_vibeScale = 100; // 100 = 1.0x, 150 = 1.5x (P 명령으로 설정)

// 진동 수신 시 LED 표시
struct VibeLedColor { uint8_t r, g, b; };
static VibeLedColor g_vibeLedColor = {0, 0, 0};
static uint32_t     g_vibeLedEndMs = 0;

// V 명령 번호별 LED 색상
static const VibeLedColor VIBE_COLORS[] = {
    {0,   0,   0  }, // V0 Stop    — 꺼짐
    {255, 0,   0  }, // V1 Danger  — 빨강
    {0,   255, 0  }, // V2 Success — 초록
    {0,   200, 255}, // V3 Correct — 하늘
    {255, 80,  0  }, // V4 Wrong   — 주황
    {0,   80,  255}, // V5 Walk    — 파랑
    {200, 200, 200}, // V6 Ready   — 흰색
};

void vibeSet(uint8_t val) { analogWrite(PIN_MOTOR, val); }

void playVibe(const VibeStep *p, int n, int vibeId = 0)
{
    if (n <= 0 || n > 10) return;
    uint32_t totalMs = 0;
    for (int i = 0; i < n; i++)
    {
        g_vSeq[i].s  = p[i].s;
        g_vSeq[i].ms = (uint16_t)min((uint32_t)p[i].ms * g_vibeScale / 100, (uint32_t)9999);
        totalMs += g_vSeq[i].ms;
    }
    g_vCount = n; g_vIdx = 0;
    g_vEnd = millis() + g_vSeq[0].ms;
    vibeSet(g_vSeq[0].s);

    // 즉시 LED 점등 — updateRGB 400ms 스로틀을 우회하기 위해 여기서 직접 setRGB 호출
    // 최소 800ms 보장 (짧은 패턴도 LED로 확인 가능)
    if (vibeId >= 0 && vibeId <= 6)
    {
        g_vibeLedColor  = VIBE_COLORS[vibeId];
        g_vibeLedEndMs  = millis() + max(totalMs, (uint32_t)800);
        setRGB(g_vibeLedColor.r, g_vibeLedColor.g, g_vibeLedColor.b);
    }
}

bool vibeIsPlaying() { return g_vIdx < g_vCount; }

void vibeStop() { g_vCount = g_vIdx = 0; vibeSet(0); }

void updateVibe()
{
    if (g_vIdx >= g_vCount) return;
    if ((uint32_t)(millis() - g_vEnd) >= 0x80000000UL) return;
    if (++g_vIdx >= g_vCount) { vibeSet(g_brake ? 180 : 0); return; }
    vibeSet(g_vSeq[g_vIdx].s);
    g_vEnd = millis() + g_vSeq[g_vIdx].ms;
}

void setBrake(bool on)
{
    g_brake = on;
    if (!vibeIsPlaying()) vibeSet(on ? 180 : 0);
}

// ── RGB 상태 ─────────────────────────────────────────────────────
enum RgbState { RGB_IDLE, RGB_RUNNING, RGB_EVENT, RGB_QUIZ };
RgbState g_rgbState = RGB_IDLE;

// ── 인터럽트 (PAS) ───────────────────────────────────────────────
void IRAM_ATTR onPasPulse()
{
    uint32_t now = micros();
    g_pulseIntervalUs = now - g_lastPulseUs;
    g_lastPulseUs = now;
}

// ── DMP Yaw 조향 처리 ────────────────────────────────────────────
void processDMP()
{
    icm_20948_DMP_data_t data;
    icm.readDMPdataFromFIFO(&data);

    if ((icm.status == ICM_20948_Stat_Ok) || (icm.status == ICM_20948_Stat_FIFOMoreDataAvail))
    {
        if ((data.header & DMP_header_bitmap_Quat6) > 0)
        {
            double q1 = ((double)data.Quat6.Data.Q1) / 1073741824.0;
            double q2 = ((double)data.Quat6.Data.Q2) / 1073741824.0;
            double q3 = ((double)data.Quat6.Data.Q3) / 1073741824.0;
            double q0 = sqrt(1.0 - ((q1*q1) + (q2*q2) + (q3*q3)));

            double t3 = +2.0 * (q0 * q3 + q1 * q2);
            double t4 = +1.0 - 2.0 * (q2 * q2 + q3 * q3);
            float yaw = atan2(t3, t4) * 180.0 / PI;

            if (!g_dmpStable)
            {
                g_yawOffset = yaw;
                if (millis() - g_bootMs > 3000)
                {
                    g_dmpStable = true;
                    Serial.println("{\"debug\":\"DMP Stabilized\"}");
                }
                return;
            }

            if (fabsf(yaw - g_lastRawYaw) > 0.25f)
            {
                g_lastRawYaw = yaw;
                g_steerAngle = yaw - g_yawOffset;
            }
        }
    }
}

void calibrateSteering()
{
    g_yawOffset += g_steerAngle;
    g_steerAngle = 0.0f;
    Serial.printf("{\"calibrated\":true,\"center\":0.0}\n");
}

float calcCadenceRPM()
{
    portDISABLE_INTERRUPTS();
    uint32_t interval = g_pulseIntervalUs;
    uint32_t last     = g_lastPulseUs;
    portENABLE_INTERRUPTS();
    if (micros() - last > PAS_TIMEOUT || interval == 0) return 0.0f;
    return constrain(60.0f / ((float)interval / 1e6f * (float)PAS_MAGNETS), 0.0f, RPM_MAX);
}

// ── RGB LED 제어 ─────────────────────────────────────────────────
void updateRGB(float spd, bool brake)
{
    uint32_t now = millis();

    // 진동 LED 활성 중 — g_dmpStable·400ms 스로틀보다 앞에서 체크하여 덮어쓰기 방지
    if (g_vibeLedEndMs > 0 && now < g_vibeLedEndMs) return;

    if (!g_dmpStable) return;

    static uint32_t blinkMs = 0;
    static bool blinkOn = false;
    if (now - blinkMs < 400) return;
    blinkMs = now;
    blinkOn = !blinkOn;

    if (brake)                    { setRGB(255, 80, 0); return; }
    if (g_rgbState == RGB_EVENT)  { setRGB(255, 0, 0);  return; }
    if (g_rgbState == RGB_QUIZ)   { setRGB(blinkOn ? 100 : 0, 0, blinkOn ? 100 : 0); return; }
    if (spd > 1.0f)               { setRGB(0, 200, 0);  return; }
    setRGB(blinkOn ? 40 : 0, blinkOn ? 40 : 0, blinkOn ? 40 : 0);
}

// ── 시리얼 명령 처리 ─────────────────────────────────────────────
void handleCommand(const String &cmd)
{
    if (cmd.length() < 1) return;
    char t = cmd.charAt(0);
    int  v = (cmd.length() > 1) ? cmd.substring(1).toInt() : 0;
    switch (t)
    {
    case 'C': calibrateSteering(); break;
    case 'M': Serial.println("{\"magcal\":\"not_required_in_dmp\"}"); break;
    case 'S': g_rgbState = (RgbState)constrain(v, 0, 3); break;
    case 'B': setBrake(v == 1); break;
    case 'P': g_vibeScale = (uint16_t)constrain(v, 50, 300); break;
    case 'V':
        // 수신 확인 에코 — Unity 콘솔에서 명령 도달 여부 확인용
        Serial.printf("{\"debug\":\"V%d recv\"}\n", v);
        switch (v)
        {
        case 0: vibeStop(); g_vibeLedEndMs = 0; break;
        case 1: playVibe(PAT_DANGER,  sizeof(PAT_DANGER)  / sizeof(VibeStep), 1); break;
        case 2: playVibe(PAT_SUCCESS, sizeof(PAT_SUCCESS) / sizeof(VibeStep), 2); break;
        case 3: playVibe(PAT_CORRECT, sizeof(PAT_CORRECT) / sizeof(VibeStep), 3); break;
        case 4: playVibe(PAT_WRONG,   sizeof(PAT_WRONG)   / sizeof(VibeStep), 4); break;
        case 5: playVibe(PAT_WALK,    sizeof(PAT_WALK)    / sizeof(VibeStep), 5); break;
        case 6: playVibe(PAT_READY,   sizeof(PAT_READY)   / sizeof(VibeStep), 6); break;
        }
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
            if (g_rxBuf.length() > 0) handleCommand(g_rxBuf);
            g_rxBuf = "";
        }
        else if (g_rxBuf.length() < 16)
        {
            g_rxBuf += c;
        }
    }
}

// ── setup ────────────────────────────────────────────────────────
void setup()
{
    Serial.begin(115200);
    delay(1000);

    Wire.begin(PIN_SDA, PIN_SCL);
    Wire.setClock(100000);

#if USE_RGB_LED
    rgb.begin();
    rgb.setBrightness(40);
    setRGB(40, 40, 40);
#endif

    pinMode(PIN_PAS, INPUT_PULLUP);
    attachInterrupt(digitalPinToInterrupt(PIN_PAS), onPasPulse, FALLING);
    pinMode(PIN_BRK,   INPUT_PULLUP);
    pinMode(PIN_BTN_O, INPUT_PULLUP);
    pinMode(PIN_BTN_X, INPUT_PULLUP);
    pinMode(PIN_MOTOR, OUTPUT);
    vibeSet(0);

    bool initialized = false;
    while (!initialized)
    {
        icm.begin(Wire, 1); // 0x69 (AD0=HIGH)
        if (icm.status != ICM_20948_Stat_Ok)
        {
            Serial.println("{\"debug\":\"Connect failed\"}");
            delay(500);
            continue;
        }
        if (icm.initializeDMP() != ICM_20948_Stat_Ok)
        {
            Serial.println("{\"debug\":\"DMP Init failed. Check ICM_20948_USE_DMP in library.\"}");
            delay(1000);
            continue;
        }
        if (icm.enableDMPSensor(INV_ICM20948_SENSOR_GAME_ROTATION_VECTOR) != ICM_20948_Stat_Ok)
        {
            Serial.println("{\"debug\":\"Sensor enable failed\"}");
            delay(500);
            continue;
        }
        if (icm.setDMPODRrate(DMP_ODR_Reg_Quat6, 4) != ICM_20948_Stat_Ok)
        {
            Serial.println("{\"debug\":\"ODR set failed\"}");
            delay(500);
            continue;
        }
        icm.enableFIFO();
        icm.enableDMP();
        icm.resetDMP();
        icm.resetFIFO();
        initialized = true;
    }

    g_bootMs = millis();
    Serial.println("{\"debug\":\"DMP v5.8 Ready. Stabilizing for 3s...\"}");
    setRGB(0, 0, 255);
}

// ── loop ─────────────────────────────────────────────────────────
void loop()
{
    uint32_t now = millis();
    processDMP();
    processSerial();
    updateVibe();

    if (now - g_lastSendMs < (1000u / SERIAL_HZ)) return;
    g_lastSendMs = now;

    float rpm  = calcCadenceRPM();
    float spd  = rpm * CADENCE_TO_KPH;
    bool  brk  = !digitalRead(PIN_BRK);
    bool  btnO = !digitalRead(PIN_BTN_O);
    bool  btnX = !digitalRead(PIN_BTN_X);

    updateRGB(spd, brk);

    Serial.printf("{\"id\":%d,\"rpm\":%.1f,\"spd\":%.1f,\"str\":%.1f,"
                  "\"brk\":%d,\"o\":%d,\"x\":%d}\n",
                  STATION_ID, rpm, spd,
                  constrain(g_steerAngle, -STEER_RANGE_DEG, STEER_RANGE_DEG),
                  brk ? 1 : 0, btnO ? 1 : 0, btnX ? 1 : 0);
}
