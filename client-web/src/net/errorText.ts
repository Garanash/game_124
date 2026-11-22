// Rules report errors with stable codes and English developer text; players see these instead.
const RUSSIAN_ERRORS: Record<string, string> = {
  "combat.cooldown": "Умение ещё перезаряжается.",
  "combat.finished": "Бой уже закончился.",
  "combat.invalid_target": "Неподходящая цель.",
  "combat.no_action": "Действие на этот ход уже потрачено.",
  "combat.no_movement": "Перемещение на этот ход закончилось.",
  "combat.no_slot": "Нет свободной ячейки заклинаний.",
  "combat.not_your_turn": "Сейчас не ваш ход.",
  "combat.out_of_range": "Цель слишком далеко — подойдите ближе.",
  "combat.unknown_ability": "Вы не знаете этого умения.",
  "combat.unknown_command": "Неизвестная команда.",
  "combat.unreachable": "Туда не дойти за этот ход.",
  "combat.cannot_engage": "Цель недоступна для атаки.",
  "combat.cannot_join": "К этому бою нельзя присоединиться.",
  "backpack.out_of_bounds": "Предмет не помещается в рюкзак здесь.",
  "backpack.overlap": "Эти клетки рюкзака заняты.",
  "backpack.missing": "Этого предмета уже нет в рюкзаке.",
  "backpack.not_found": "Этого предмета уже нет в рюкзаке.",
  "backpack.unknown_item": "Неизвестный предмет.",
  "bag.missing": "Этого предмета нет в сумке.",
  "bag.insufficient": "В сумке не хватает предметов.",
  "fusion.unavailable": "Для слияния предметы должны касаться друг друга.",
  "fusion.no_space": "Не хватает места для нового предмета.",
};

export function playerErrorText(code: string, serverText: string): string {
  return RUSSIAN_ERRORS[code] ?? serverText;
}
