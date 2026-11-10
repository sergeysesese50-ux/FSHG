// Точка входа серверной логики: Nakama вызывает InitModule один раз при старте.
// RPC регистрируются только функциями, объявленными на верхнем уровне файла,
// стрелочные и анонимные функции Nakama не примет.

function InitModule(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  initializer: nkruntime.Initializer
): void {
  const fish = loadFishData(nk);
  logger.info('Сервер %s: загружено видов рыбы — %d', ctx.env['SERVER_ID'], fish.length);

  initializer.registerRpc('healthcheck', rpcHealthcheck);
}

// Проверка, что сервер жив и видит общие данные. Вызывается клиентом после входа.
function rpcHealthcheck(
  ctx: nkruntime.Context,
  logger: nkruntime.Logger,
  nk: nkruntime.Nakama,
  payload: string
): string {
  const fish = loadFishData(nk);
  return JSON.stringify({
    status: 'ok',
    server: ctx.env['SERVER_ID'],
    region: ctx.env['SERVER_REGION'],
    fishSpecies: fish.length,
    serverTimeMs: Date.now()
  });
}
