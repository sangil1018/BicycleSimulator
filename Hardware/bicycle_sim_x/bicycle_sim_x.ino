// ================================================================
//  자전거 안전체험 시뮬레이터  ESP32-S3 펌웨어 v6.2
//  빛고을국민안전체험관 / FLUXION / 2026.07
//  v5.8: 진동 제어 ESP32 통합 (IRF520 모듈, GPIO2 PWM)
//  v5.9: 하트비트(H) 에코 추가, PAS micros() 랩어라운드 가드
//  v6.0: PAS 진단 필드 추가 — pc(누적 펄스 수)/pl(핀 레벨).
//        "PAS만 무반응" 현장 진단용: 페달을 돌려도 pc가 늘지 않으면
//        센서 전원/배선 문제, pc는 느는데 rpm=0이면 펌웨어 문제.
//  v6.1: PAS 재초기화 명령(R) 추가 — 인터럽트 재등록으로 "버튼은 살아있는데
//        PAS만 죽는" 현상을 보드 리셋 없이 복구. Unity가 레벨 진입 시 송신.
//  v6.2: 태스크 워치독(5s) 추가 — I2C 버스 락업 등으로 loop가 굳으면 자동 리셋.
//        부팅 시 reset_reason 송신 — TASK_WDT(6)가 반복되면 loop 정지를 의심할 것.
// ================================================================

#include <Wire.h>
#include <ICM_20948.h>
#include <esp_task_wdt.h>
#include <esp_system.h>
#include "vibe_types.h"

// ── 워치독 ───────────────────────────────────────────────────────
// loop()가 이 시간 안에 한 바퀴를 못 돌면 보드를 자동 리셋한다.
// 무인 키오스크의 최대 리스크는 I2C 버스 락업이다 — ICM-20948의 FIFO를 읽는
// readDMPdataFromFIFO()가 버스 락업 시 그 자리에서 멈추면 보드가 통째로 굳고,
// 텔레메트리가 끊겨도 Unity의 재연결로는 절대 복구되지 않는다(전원 재인가만이 답).
// 워치독이 물면 리셋 → setup() 재실행 → I2C 재초기화로 스스로 살아난다.
#define WDT_TIMEOUT_MS 5000

// 펌웨어 버전 — 'I'(정보 조회) 명령의 응답에 실린다.
// 부팅 메시지는 setup()에서 한 번만 나가는데, 이 보드는 포트를 열어도 리셋되지 않으므로
// 나중에 접속한 Unity/모니터는 그걸 영영 못 본다. 그래서 "지금 어떤 펌웨어가 올라가 있고,
// 워치독은 걸렸고, 왜 마지막에 리셋됐는지"를 언제든 물어볼 수 있는 경로가 필요하다.
#define FW_VERSION "6.2"

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

// I2C 클럭 (조향 센서 ICM-20948). DMP FIFO를 loop마다 읽으므로 100kHz에서는 I2C가
// 병목이 된다. 400000(fast mode)이 기본값.
// ── 조향이 튀거나 멈추거나 부팅 시 "Connect failed"가 뜨면 100000으로 되돌릴 것. ──
// 배선이 길거나 노이즈가 심한 환경에서 400kHz가 불안정할 수 있으며,
// 그 경우 100kHz로 낮추는 것 외에 다른 코드는 손댈 필요가 없다.
#define I2C_CLOCK_HZ 400000
#define RPM_MAX        300.0f
#define CADENCE_TO_KPH   0.25f
#define SERIAL_HZ       50
#define STEER_RANGE_DEG 45.0f
#define PAS_TIMEOUT     500000UL

// ── 전역 변수 ────────────────────────────────────────────────────
volatile uint32_t g_lastPulseUs    = 0;
volatile uint32_t g_pulseIntervalUs = UINT32_MAX;
volatile uint32_t g_pulseCount     = 0;   // 부팅 후 누적 PAS 펄스 수 (진단용)
float    g_steerAngle  = 0.0f;
float    g_lastRawYaw  = 0.0f;
float    g_yawOffset   = 0.0f;
// 수신 명령 버퍼 — Arduino String 대신 고정 배열을 쓴다.
// String의 += / = "" / substring()은 매번 힙을 재할당해, 수개월 연속 가동하는
// 키오스크에서는 힙 단편화로 이어질 수 있다. 명령은 최대 16자로 충분하다.
char     g_rxBuf[17]   = {0};
uint8_t  g_rxLen       = 0;
uint32_t g_lastSendMs  = 0;
bool     g_dmpStable   = false;
uint32_t g_bootMs      = 0;
bool     g_icmOk       = false;  // 조향 센서 인식 여부 — 실패 시 str은 항상 0으로 송신
int      g_resetReason = 0;     // 부팅 시 기록 — 'I' 명령으로 나중에 조회 가능
bool     g_wdtArmed    = false; // 워치독 등록 성공 여부

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
    g_pulseCount++;
}

// ── DMP Yaw 조향 처리 ────────────────────────────────────────────

// 각도 차를 [-180, 180]으로 정규화한다.
// yaw는 atan2 결과라 ±180에서 불연속이다. 정규화 없이 그냥 빼면, 기준점이 경계
// 근처에 앉았을 때 조향각이 +45에서 -45로 튀어버린다(핸들이 반대로 확 꺾인 것처럼).
static inline float normalizeAngle(float deg)
{
    while (deg >  180.0f) deg -= 360.0f;
    while (deg < -180.0f) deg += 360.0f;
    return deg;
}

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

            // 데드밴드 비교도 정규화된 차이로 해야 한다 — 경계를 넘나들 때
            // 실제로는 0.1도 움직였는데 359.9도 움직인 것으로 보이는 것을 막는다.
            if (fabsf(normalizeAngle(yaw - g_lastRawYaw)) > 0.25f)
            {
                g_lastRawYaw = yaw;
                g_steerAngle = normalizeAngle(yaw - g_yawOffset);
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
    if (micros() - last > PAS_TIMEOUT || interval == 0)
    {
        // 타임아웃 시 interval 무효화 — micros() 32비트 랩(약 71.6분 주기)으로
        // 오래된 타임스탬프가 유효 창에 다시 들어와 유령 속도가 튀는 것을 방지.
        // 사이에 새 펄스가 들어왔으면(g_lastPulseUs 변경) 건드리지 않는다.
        portDISABLE_INTERRUPTS();
        if (g_lastPulseUs == last) g_pulseIntervalUs = UINT32_MAX;
        portENABLE_INTERRUPTS();
        return 0.0f;
    }
    if (interval == UINT32_MAX) return 0.0f; // 무효화 직후 첫 펄스 대기 상태
    return constrain(60.0f / ((float)interval / 1e6f * (float)PAS_MAGNETS), 0.0f, RPM_MAX);
}

// PAS 입력 재초기화 — 인터럽트를 떼었다 다시 걸고 펄스 상태를 초기화한다.
// 장시간 가동 중 PAS 인터럽트만 죽어 rpm=0으로 굳는 현상(버튼/브레이크는 정상)의
// 복구용. 보드 리셋과 달리 DMP·조향 보정 상태를 유지하므로 입력 공백이 없다.
void reinitPasInput()
{
    detachInterrupt(digitalPinToInterrupt(PIN_PAS));
    pinMode(PIN_PAS, INPUT_PULLUP);

    portDISABLE_INTERRUPTS();
    uint32_t prevCount    = g_pulseCount;
    g_lastPulseUs         = micros();
    g_pulseIntervalUs     = UINT32_MAX;   // 첫 펄스 전까지 rpm=0
    g_pulseCount          = 0;
    portENABLE_INTERRUPTS();

    attachInterrupt(digitalPinToInterrupt(PIN_PAS), onPasPulse, FALLING);

    // prev는 재초기화 직전까지의 누적 펄스 — 0이면 부팅 후 한 번도 안 들어온 것이므로
    // 인터럽트가 아니라 센서 전원/배선을 의심해야 한다.
    Serial.printf("{\"debug\":\"pas_reinit\",\"prev\":%lu,\"pl\":%d}\n",
                  (unsigned long)prevCount, digitalRead(PIN_PAS) ? 1 : 0);
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
void handleCommand(const char *cmd)
{
    if (cmd[0] == '\0') return;
    char t = cmd[0];
    int  v = atoi(cmd + 1); // 숫자가 없으면 0 — 기존 substring().toInt()와 동일
    switch (t)
    {
    case 'C': calibrateSteering(); break;
    case 'R': reinitPasInput(); break;                     // PAS 인터럽트 재등록 (Unity가 레벨 진입 시 송신)
    case 'I': reportInfo(); break;                         // 펌웨어 버전/워치독/리셋 원인 조회
    case 'H': Serial.println("{\"debug\":\"hb\"}"); break; // Unity keep-alive 에코 (10초 주기)
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
            g_rxBuf[g_rxLen] = '\0';
            if (g_rxLen > 0) handleCommand(g_rxBuf);
            g_rxLen = 0;
        }
        else if (c != ' ' && c != '\t' && g_rxLen < sizeof(g_rxBuf) - 1)
        {
            // 공백은 버린다 (기존 String.trim()과 동일한 효과).
            // 버퍼가 차면 이후 문자는 버려 오버런을 막는다.
            g_rxBuf[g_rxLen++] = c;
        }
    }
}

// ── 워치독 등록 ──────────────────────────────────────────────────
// setup()의 ICM 재시도 루프가 최대 5초 이상 delay()를 쓰므로, 워치독은 반드시
// setup() 끝에서 등록한다. 먼저 등록하면 부팅 중에 스스로 리셋을 물어버린다.
void setupWatchdog()
{
    esp_task_wdt_config_t cfg;
    cfg.timeout_ms     = WDT_TIMEOUT_MS;
    cfg.idle_core_mask = 0;     // idle 태스크는 감시하지 않는다 — loop만 본다
    cfg.trigger_panic  = true;  // 물면 panic → 리셋

    // Arduino-ESP32 3.x는 부팅 시 TWDT를 이미 초기화해 둔다. 그 경우 init()은
    // ESP_ERR_INVALID_STATE를 내므로 reconfigure()를 먼저 시도하고, 초기화가
    // 안 된 환경(2.x 등)이면 init()으로 넘어간다.
    esp_err_t err = esp_task_wdt_reconfigure(&cfg);
    if (err == ESP_ERR_INVALID_STATE) err = esp_task_wdt_init(&cfg);

    if (err != ESP_OK)
    {
        Serial.printf("{\"debug\":\"wdt_setup_failed\",\"err\":%d}\n", (int)err);
        return;
    }

    esp_task_wdt_add(NULL); // 현재 태스크(loopTask)를 감시 대상으로 등록
    g_wdtArmed = true;
    Serial.printf("{\"debug\":\"wdt_armed\",\"timeout_ms\":%d}\n", WDT_TIMEOUT_MS);
}

// 'I' 명령 응답 — 지금 올라가 있는 펌웨어의 상태를 통째로 알려준다.
// 부팅 메시지를 놓친 접속자(Unity·모니터)가 재플래싱 여부와 워치독·리셋 원인을
// 확인할 수 있는 유일한 경로다.
void reportInfo()
{
    Serial.printf(
        "{\"debug\":\"info\",\"fw\":\"%s\",\"reset_reason\":%d,\"wdt\":%d,"
        "\"i2c\":%d,\"steer\":%d,\"pc\":%lu}\n",
        FW_VERSION, g_resetReason, g_wdtArmed ? 1 : 0,
        I2C_CLOCK_HZ, g_icmOk ? 1 : 0, (unsigned long)g_pulseCount);
}

// ── setup ────────────────────────────────────────────────────────
void setup()
{
    Serial.begin(115200);
    delay(1000);

    // 리셋 원인을 남긴다 — 현장에서 "워치독이 실제로 물었는지"를 알 수 있는 유일한 단서다.
    // esp_reset_reason_t: 1=POWERON, 3=SW, 4=PANIC, 5=INT_WDT, 6=TASK_WDT, 7=WDT, 8=DEEPSLEEP, 9=BROWNOUT
    // TASK_WDT(6)가 반복되면 loop가 어딘가에서 굳고 있다는 뜻 — I2C 락업을 의심할 것.
    // 이 줄은 한 번만 나가므로 나중에 접속한 쪽은 못 본다 — 그래서 값을 보관해 'I'로 답한다.
    g_resetReason = (int)esp_reset_reason();
    Serial.printf("{\"debug\":\"boot\",\"reset_reason\":%d}\n", g_resetReason);

    Wire.begin(PIN_SDA, PIN_SCL);
    Wire.setClock(I2C_CLOCK_HZ); // 조정은 파일 상단의 I2C_CLOCK_HZ에서

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

    // 센서 인식 실패 시에도 부팅을 계속해 str=0으로 송신 (재시도 5회 후 포기)
    const uint8_t ICM_MAX_RETRY = 5;
    for (uint8_t attempt = 0; attempt < ICM_MAX_RETRY; attempt++)
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
        g_icmOk = true;
        break;
    }

    g_bootMs = millis();
    if (g_icmOk)
    {
        Serial.println("{\"debug\":\"DMP v6.0 Ready. Stabilizing for 3s...\"}");
        setRGB(0, 0, 255);
    }
    else
    {
        g_steerAngle = 0.0f;
        g_dmpStable  = true; // 안정화 대기 없이 LED 상태 표시 활성화
        Serial.println("{\"debug\":\"Steer sensor NOT found. str fixed to 0\"}");
        setRGB(255, 0, 255); // 자홍색: 센서 없음 경고
    }

    setupWatchdog(); // 반드시 마지막 — 위의 재시도 delay()들이 워치독을 물지 않도록
}

// ── loop ─────────────────────────────────────────────────────────
void loop()
{
    // 워치독 먹이주기 — loop가 정상적으로 돌고 있다는 신호.
    // I2C 락업 등으로 아래 어딘가에서 굳으면 WDT_TIMEOUT_MS 뒤 자동 리셋된다.
    esp_task_wdt_reset();

    uint32_t now = millis();
    if (g_icmOk) processDMP();
    processSerial();
    updateVibe();

    if (now - g_lastSendMs < (1000u / SERIAL_HZ)) return;
    g_lastSendMs = now;

    float rpm  = calcCadenceRPM();
    float spd  = rpm * CADENCE_TO_KPH;
    bool  brk  = digitalRead(PIN_BRK);   // 브레이크는 normally-closed 스위치 — 눌리면 열려서 풀업으로 HIGH
    bool  btnO = !digitalRead(PIN_BTN_O);
    bool  btnX = !digitalRead(PIN_BTN_X);

    updateRGB(spd, brk);

    // pc/pl은 PAS 진단용 — Unity JsonUtility는 모르는 필드를 무시하므로 게임에 영향 없음
    Serial.printf("{\"id\":%d,\"rpm\":%.1f,\"spd\":%.1f,\"str\":%.1f,"
                  "\"brk\":%d,\"o\":%d,\"x\":%d,\"pc\":%lu,\"pl\":%d}\n",
                  STATION_ID, rpm, spd,
                  constrain(g_steerAngle, -STEER_RANGE_DEG, STEER_RANGE_DEG),
                  brk ? 1 : 0, btnO ? 1 : 0, btnX ? 1 : 0,
                  (unsigned long)g_pulseCount, digitalRead(PIN_PAS) ? 1 : 0);
}
