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
  "equipment.not_in_backpack": "Этого предмета уже нет в рюкзаке.",
  "equipment.wrong_slot": "Этот предмет сюда не надеть.",
  "equipment.backpack_full": "В рюкзаке нет места для снятого предмета.",
  "equipment.empty_slot": "В этом слоте ничего нет.",
  "point_buy.max": "Эта характеристика уже на максимуме для распределения.",
  "point_buy.no_points": "Свободных очков не осталось.",
};

export function playerErrorText(code: string, serverText: string): string {
  return RUSSIAN_ERRORS[code] ?? serverText;
}
