// soem_wrap.c
#include <stdint.h>
#include <string.h>

#define WIN32_LEAN_AND_MEAN
#include <winsock2.h>
#include <ws2tcpip.h>

#ifdef IN_MULTICAST
#undef IN_MULTICAST
#endif

#ifdef _WIN32
#define EXP  __declspec(dllexport)
#define CALL __cdecl
#else
#define EXP
#define CALL
#endif

#include <soem/soem.h>  //SOEM

static ecx_contextt g_ctx;
static int g_inited = 0;

// IOmap 크기는 필요 시 늘리세요.
static uint8_t IOmap[8192];

EXP int CALL soem_open(const char *ifname)
{
   memset(&g_ctx, 0, sizeof(g_ctx));

   // v2.0: ecx_init(context, ifname) 만 사용
   if (ecx_init(&g_ctx, (char *)ifname) > 0)
   {
      g_inited = 1;
      return 0;
   }
   return -1;
}

EXP void CALL soem_close(void)
{
   if (g_inited)
   {
      ecx_close(&g_ctx);
      g_inited = 0;
   }
}

EXP int CALL soem_config_init(int use_map)
{
   if (!g_inited) return -2;

   // v2.0: ecx_config_init(context) ← 인자 1개
   if (ecx_config_init(&g_ctx) <= 0) 
       return -1;

   if (use_map)
   {
      int iomap = ecx_config_map_group(&g_ctx, IOmap, 0);
      if (iomap <= 0) return -3;
   }
   return 0;
}

EXP int CALL soem_config_map_only(void)
{
   if (!g_inited)
      return -2;

   int iomap = ecx_config_map_group(&g_ctx, IOmap, 0);
   if (iomap <= 0)
      return -1;

   return iomap; // 맵 크기 바이트 수 리턴 (원하면 그냥 0/에러로 해도 됨)
}


EXP int CALL soem_set_state(uint16_t state, int timeout_ms)
{
   if (!g_inited) return -2;
   g_ctx.slavelist[0].state = state;
   ecx_writestate(&g_ctx, 0);
   return (ecx_statecheck(&g_ctx, 0, state, timeout_ms * 1000) == state) ? 0 : -1;
}

EXP int CALL soem_slave_count(void)
{
   return g_ctx.slavecount;
}

EXP unsigned short CALL soem_slave_al_status(int i)
{
   if (i < 1 || i > g_ctx.slavecount)
      return 0;
   return g_ctx.slavelist[i].ALstatuscode;
}

typedef struct soem_slave_info_t
{
   uint16 alias;              // Station Alias
   uint16 configadr;          // Station Address (논리 주소)
   uint32 vendor;             // eep_man
   uint32 product;            // eep_id
   uint32 revision;            // revision
   char name[EC_MAXNAME + 1]; // 슬레이브 이름(ESI)
} soem_slave_info_t;

EXP int CALL soem_get_slave_info(int idx, soem_slave_info_t *outInfo)
{
   if (!outInfo) return 0;
   if (idx < 1 || idx > g_ctx.slavecount) return 0;

   ec_slavet *s = &g_ctx.slavelist[idx];

   outInfo->alias = s->aliasadr;
   outInfo->configadr = s->configadr;
   outInfo->vendor = s->eep_man;
   outInfo->product = s->eep_id;
   outInfo->revision = s->eep_rev; // ★ 여기

   // 이름 복사
   //strncpy(outInfo->name, s->name, EC_MAXNAME);
   strncpy_s(outInfo->name, EC_MAXNAME + 1, s->name, _TRUNCATE);
   outInfo->name[EC_MAXNAME] = '\0';

   return 1;
}


// --------- CoE SDO ----------
EXP int CALL soem_sdo_read(uint16_t slave, uint16_t index, uint8_t subindex, void *buf, uint32_t *inout_len)
{
   int len = (int)*inout_len;
   int wkc = ecx_SDOread(&g_ctx, slave, index, subindex, FALSE, &len, buf, EC_TIMEOUTRXM);
   if (wkc <= 0) 
       return -1;
   *inout_len = (uint32_t)len;
   return 0;
}

EXP int CALL soem_sdo_write(uint16_t slave, uint16_t index, uint8_t subindex, const void *data, uint32_t len)
{
   int wkc = ecx_SDOwrite(&g_ctx, slave, index, subindex, FALSE, (int)len, (void *)data, EC_TIMEOUTRXM);
   return (wkc > 0) ? 0 : -1;
}

// --------- PDO 주기 ----------
EXP int CALL soem_send_processdata(void)
{
   return ecx_send_processdata(&g_ctx);
}
EXP int CALL soem_receive_processdata(int timeout_us)
{
   return ecx_receive_processdata(&g_ctx, timeout_us);
}

//PDO 직접 접근 유틸
EXP int CALL soem_write_u16(uint16_t s, int off, uint16_t v)
{
   memcpy(g_ctx.slavelist[s].outputs + off, &v, 2);
   return 0;
}
EXP int CALL soem_write_s32(uint16_t s, int off, int32_t v)
{
   memcpy(g_ctx.slavelist[s].outputs + off, &v, 4);
   return 0;
}
EXP int CALL soem_read_u16(uint16_t s, int off, uint16_t *v)
{
   memcpy(v, g_ctx.slavelist[s].inputs + off, 2);
   return 0;
}
EXP int CALL soem_read_s32(uint16_t s, int off, int32_t *v)
{
   memcpy(v, g_ctx.slavelist[s].inputs + off, 4);
   return 0;
}

//Ethercat Slave 조회.
EXP void CALL soem_readstate(void)
{
   // 2.x: 컨텍스트 기반
   ecx_readstate(&g_ctx);
}

EXP unsigned short CALL soem_slave_state(int i)
{
   if (i < 1 || i > g_ctx.slavecount) 
       return 0;
   return g_ctx.slavelist[i].state;
}

EXP const char *CALL soem_slave_name(int i)
{
   if (i < 1 || i > g_ctx.slavecount) 
       return "";
   return g_ctx.slavelist[i].name;
}

// ESI(EEPROM) 식별
EXP unsigned int CALL soem_slave_vendor_id(int i)
{
   if (i < 1 || i > g_ctx.slavecount) 
       return 0;
   return g_ctx.slavelist[i].eep_man;
}
EXP unsigned int CALL soem_slave_product_code(int i)
{
   if (i < 1 || i > g_ctx.slavecount) 
       return 0;
   return g_ctx.slavelist[i].eep_id;
}
EXP unsigned int CALL soem_slave_revision(int i)
{
   if (i < 1 || i > g_ctx.slavecount) 
       return 0;
   return g_ctx.slavelist[i].eep_rev;
}

