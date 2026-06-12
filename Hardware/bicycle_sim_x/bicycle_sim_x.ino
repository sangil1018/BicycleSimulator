// ================================================================
//  자전거 안전체험 시뮬레이터  ESP32-S3 펌웨어 v5.6 (Roll 조향)
//  빛고을국민안전체험관 / FLUXION / 2026.06
//  v5.6: v5.5 기반 — DMP Quat6 Yaw 값으로 조향각 계산
//        (센서 수평 장착, 핸들 좌우 회전 방식)
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
#define PIN_PAS 1
#define PIN_VIBE_L 2
#define PIN_VIBE_R 42
#define PIN_BRK_L 4
#define PIN_BRK_R 5
#define PIN_BTN_O 6
#define PIN_BTN_X 7
#define PIN_SDA 17
#define PIN_SCL 18

// ── 시스템 상수 ─────────────────────────────────────────────────
#define STATION_ID 1
#define PAS_MAGNETS 12
#define RPM_MAX 300.0f
#define CADENCE_TO_KPH 0.25f
#define SERIAL_HZ 50
#define STEER_RANGE_DEG 45.0f
#define PAS_TIMEOUT 500000UL
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
float g_steerAngle = 0.0f;
float g_lastRawYaw = 0.0f;
float g_yawOffset = 0.0f;
String g_rxBuf = "";
uint32_t g_lastSendMs = 0;
bool g_dmpStable = false;
uint32_t g_bootMs = 0;

// ── RGB 상태 ─────────────────────────────────────────────────────
enum RgbState
{
    RGB_IDLE,
    RGB_RUNNING,
    RGB_EVENT,
    RGB_QUIZ
};
RgbState g_rgbState = RGB_IDLE;

// ── 인터럽트 (PAS) ────────────────────────────────────────────────
void IRAM_ATTR onPasPulse()
{
    uint32_t now = micros();
    g_pulseIntervalUs = now - g_lastPulseUs;
    g_lastPulseUs = now;
}

// ── 진동 제어 ─────────────────────────────────────────────────────
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
        return;
    if (++g_vIdx >= g_vCount)
    {
        vibeSet(0);
        return;
    }
    vibeSet(g_vSeq[g_vIdx].s);
    g_vEnd = millis() + g_vSeq[g_vIdx].ms;
}

// ── DMP Yaw 조향 처리 (수평 장착 기준) ───────────────────────────────
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
            double q0 = sqrt(1.0 - ((q1 * q1) + (q2 * q2) + (q3 * q3)));

            // Yaw: Z축 회전 (핸들 좌우 조향, 수평 장착 기준)
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
    uint32_t last = g_lastPulseUs;
    portENABLE_INTERRUPTS();
    if (micros() - last > PAS_TIMEOUT || interval == 0)
        return 0.0f;
    return constrain(60.0f / ((float)interval / 1e6f * (float)PAS_MAGNETS), 0.0f, RPM_MAX);
}

// ── RGB LED 제어 ─────────────────────────────────────────────────────
void updateRGB(float spd, bool brakeAny)
{
    if (!g_dmpStable)
        return; // 안정화 중에는 setup에서 파란색 유지

    static uint32_t blinkMs = 0;
    static bool blinkOn = false;
    uint32_t now = millis();
    if (now - blinkMs < 400)
        return;
    blinkMs = now;
    blinkOn = !blinkOn;

    if (brakeAny)
    {
        setRGB(255, 80, 0); // 주황색 (제동)
        return;
    }
    if (g_rgbState == RGB_EVENT)
    {
        setRGB(255, 0, 0); // 빨간색 (이벤트)
        return;
    }
    if (g_rgbState == RGB_QUIZ)
    {
        setRGB(blinkOn ? 100 : 0, 0, blinkOn ? 100 : 0); // 보라색 점멸 (퀴즈)
        return;
    }
    if (spd > 1.0f)
    {
        setRGB(0, 200, 0); // 초록색 (주행)
        return;
    }
    // 대기 상태 (흰색 점멸)
    setRGB(blinkOn ? 40 : 0, blinkOn ? 40 : 0, blinkOn ? 40 : 0);
}

// ── 시리얼 명령 처리 ─────────────────────────────────────────────────
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
    case 'M': /* DMP는 지자기 보정 불요하나 응답은 유지 */
        Serial.println("{\"magcal\":\"not_required_in_dmp\"}");
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

// ── setup ──────────────────────────────────────────────────────────
void setup()
{
    Serial.begin(115200);
    delay(1000);

    Wire.begin(PIN_SDA, PIN_SCL);
    Wire.setClock(400000);

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

    ledcAttach(PIN_VIBE_L, 1000, 8);
    ledcAttach(PIN_VIBE_R, 1000, 8);
    vibeSet(0);

    bool initialized = false;
    while (!initialized)
    {
        icm.begin(Wire, 1);
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
    Serial.println("{\"debug\":\"DMP v5.6 Roll Ready. Stabilizing for 3s...\"}");
    setRGB(0, 0, 255); // 파란색: 안정화 중
}

void loop()
{
    uint32_t now = millis();
    updateVibe();
    processDMP();
    processSerial();

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
    {
        vibeSet((brkL || brkR) ? 180 : 0);
    }

    updateRGB(spd, brkL || brkR);

    Serial.printf("{\"id\":%d,\"rpm\":%.1f,\"spd\":%.1f,\"str\":%.1f,"
                  "\"brkL\":%d,\"brkR\":%d,\"o\":%d,\"x\":%d}\n",
                  STATION_ID, rpm, spd, constrain(g_steerAngle, -STEER_RANGE_DEG, STEER_RANGE_DEG),
                  brkL ? 1 : 0, brkR ? 1 : 0, btnO ? 1 : 0, btnX ? 1 : 0);
}
