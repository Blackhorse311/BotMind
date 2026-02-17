using EFT;
using EFT.Communications;
using System;
using UnityEngine;

namespace Blackhorse311.BotMind.Modules.MedicBuddy
{
    /// <summary>
    /// Displays on-screen toast notifications for MedicBuddy events.
    /// Each event has 5 randomized message variants to reduce repetitiveness.
    /// USEC players see English messages, BEAR/Scav players see Russian.
    /// Text notifications always display regardless of voice line settings.
    /// </summary>
    public static class MedicBuddyNotifier
    {
        private static readonly System.Random _random = new System.Random();

        #region English Messages (USEC)

        private static readonly string[] SummonRequestMessagesEN =
        {
            "Help requested! Stand by...",
            "Calling it in! MedicBuddy en route.",
            "Signal sent! Help is being dispatched.",
            "Copy that! Medical team is spinning up.",
            "Distress call received. Hang tight!"
        };

        private static readonly string[] TeamEnRouteMessagesEN =
        {
            "Help is on the way!",
            "Team deployed! Moving to your position.",
            "Medical team inbound. Hold your position.",
            "We've got your location. Moving in.",
            "Boots on the ground! Heading to you now."
        };

        private static readonly string[] TeamArrivedMessagesEN =
        {
            "We're here! Securing the area.",
            "On site! Setting up a perimeter.",
            "Team in position. You're covered.",
            "Area secured. Ready to treat.",
            "We've arrived. Locking down the area."
        };

        private static readonly string[] HealingStartMessagesEN =
        {
            "Hold still, I've got you.",
            "Let me take a look... I've seen worse.",
            "Easy now. I'll have you patched up in no time.",
            "Sit tight, I'm working on it.",
            "You're gonna be alright. Hold still for me."
        };

        private static readonly string[] HealingCompleteMessagesEN =
        {
            "There you go! You're all patched up. Get back in the fight!",
            "OK you stupid Gopnik! I've healed you! Now grab some semechki, some vodka, and show them what Hard-Bass can do! Cheeki Breeki Iv Damke!",
            "All done! You're combat ready. Go get 'em.",
            "That's the last of it. You're good to go!",
            "Patched up and ready to roll. Stay safe out there!"
        };

        private static readonly string[] TeamExfilMessagesEN =
        {
            "We're pulling out. Stay safe!",
            "Mission complete. Team is exfilling.",
            "Our work here is done. Good luck out there!",
            "Pulling back. You've got this from here.",
            "Team moving out. Give 'em hell!"
        };

        #endregion

        #region Russian Messages (BEAR / Scav) - Informal military speech

        private static readonly string[] SummonRequestMessagesRU =
        {
            "\u0417\u0430\u043F\u0440\u043E\u0441 \u043F\u0440\u0438\u043D\u044F\u043B. \u0416\u0434\u0438.",   // Запрос принял. Жди.
            "\u0412\u044B\u0437\u043E\u0432 \u043F\u0440\u0438\u043D\u044F\u0442! \u0413\u0440\u0443\u043F\u043F\u0430 \u043D\u0430 \u0432\u044B\u0445\u043E\u0434\u0435.",   // Вызов принят! Группа на выходе.
            "\u0421\u0438\u0433\u043D\u0430\u043B \u0435\u0441\u0442\u044C. \u0412\u044B\u0441\u044B\u043B\u0430\u044E \u043F\u043E\u043C\u043E\u0449\u044C.",   // Сигнал есть. Высылаю помощь.
            "\u041F\u0440\u0438\u043D\u044F\u043B! \u041C\u0435\u0434\u0438\u043A\u0438 \u0441\u043E\u0431\u0438\u0440\u0430\u044E\u0442\u0441\u044F.",   // Принял! Медики собираются.
            "\u041F\u0440\u0438\u043D\u044F\u043B\u0438 \u0441\u0438\u0433\u043D\u0430\u043B. \u0414\u0435\u0440\u0436\u0438\u0441\u044C, \u0431\u0440\u0430\u0442."   // Приняли сигнал. Держись, брат.
        };

        private static readonly string[] TeamEnRouteMessagesRU =
        {
            "\u0418\u0434\u0451\u043C \u043A \u0442\u0435\u0431\u0435!",   // Идём к тебе!
            "\u0413\u0440\u0443\u043F\u043F\u0430 \u043D\u0430 \u043C\u0430\u0440\u0448\u0435. \u0422\u0432\u043E\u044F \u0442\u043E\u0447\u043A\u0430 \u0435\u0441\u0442\u044C.",   // Группа на марше. Твоя точка есть.
            "\u041C\u0435\u0434\u0438\u043A\u0438 \u043D\u0430 \u043F\u043E\u0434\u0445\u043E\u0434\u0435. \u0414\u0435\u0440\u0436\u0438 \u043F\u043E\u0437\u0438\u0446\u0438\u044E.",   // Медики на подходе. Держи позицию.
            "\u0422\u0432\u043E\u044F \u0442\u043E\u0447\u043A\u0430 \u0435\u0441\u0442\u044C. \u0417\u0430\u0445\u043E\u0434\u0438\u043C.",   // Твоя точка есть. Заходим.
            "\u0412\u044B\u0434\u0432\u0438\u043D\u0443\u043B\u0438\u0441\u044C! \u0421\u043A\u043E\u0440\u043E \u0431\u0443\u0434\u0435\u043C.",   // Выдвинулись! Скоро будем.
        };

        private static readonly string[] TeamArrivedMessagesRU =
        {
            "\u041D\u0430 \u043C\u0435\u0441\u0442\u0435. \u0420\u0430\u0431\u043E\u0442\u0430\u0435\u043C \u043F\u0435\u0440\u0438\u043C\u0435\u0442\u0440.",   // На месте. Работаем периметр.
            "\u041F\u0440\u0438\u0448\u043B\u0438. \u0421\u0442\u0430\u0432\u0438\u043C \u043E\u0445\u0440\u0430\u043D\u0435\u043D\u0438\u0435.",   // Пришли. Ставим охранение.
            "\u0413\u0440\u0443\u043F\u043F\u0430 \u043D\u0430 \u0442\u043E\u0447\u043A\u0435. \u041F\u0440\u0438\u043A\u0440\u044B\u0432\u0430\u0435\u043C.",   // Группа на точке. Прикрываем.
            "\u0427\u0438\u0441\u0442\u043E. \u0413\u043E\u0442\u043E\u0432\u044B \u0440\u0430\u0431\u043E\u0442\u0430\u0442\u044C.",   // Чисто. Готовы работать.
            "\u0417\u0430\u043D\u044F\u043B\u0438 \u043F\u043E\u0437\u0438\u0446\u0438\u0438. \u0422\u044B \u0432 \u0431\u0435\u0437\u043E\u043F\u0430\u0441\u043D\u043E\u0441\u0442\u0438."   // Заняли позиции. Ты в безопасности.
        };

        private static readonly string[] HealingStartMessagesRU =
        {
            "\u041D\u0435 \u0434\u0451\u0440\u0433\u0430\u0439\u0441\u044F. \u0421\u0435\u0439\u0447\u0430\u0441 \u043F\u043E\u0447\u0438\u043D\u044E.",   // Не дёргайся. Сейчас починю.
            "\u0414\u0430\u0439 \u0433\u043B\u044F\u043D\u0443... \u0412\u0438\u0434\u0430\u043B \u0438 \u0445\u0443\u0436\u0435.",   // Дай гляну... Видал и хуже.
            "\u0422\u0438\u0445\u043E. \u0420\u0430\u0431\u043E\u0442\u0430\u044E.",   // Тихо. Работаю.
            "\u0421\u0438\u0434\u0438. \u0421\u0435\u0439\u0447\u0430\u0441 \u043F\u043E\u0434\u043B\u0430\u0442\u0430\u044E.",   // Сиди. Сейчас подлатаю.
            "\u041D\u043E\u0440\u043C\u0430\u043B\u044C\u043D\u043E \u0431\u0443\u0434\u0435\u0442. \u041D\u0435 \u0434\u0432\u0438\u0433\u0430\u0439\u0441\u044F."   // Нормально будет. Не двигайся.
        };

        private static readonly string[] HealingCompleteMessagesRU =
        {
            "\u0413\u043E\u0442\u043E\u0432. \u0417\u0430\u043B\u0430\u0442\u0430\u043B. \u0414\u0430\u0432\u0430\u0439 \u0432 \u0431\u043E\u0439.",   // Готов. Залатал. Давай в бой.
            "\u041D\u0443 \u0432\u0441\u0451, \u0433\u043E\u043F\u043D\u0438\u043A! \u041F\u043E\u0447\u0438\u043D\u0438\u043B \u0442\u0435\u0431\u044F! \u0425\u0432\u0430\u0442\u0430\u0439 \u0441\u0435\u043C\u0435\u0447\u043A\u0438, \u0432\u043E\u0434\u043A\u0443, \u0438 \u043F\u043E\u043A\u0430\u0436\u0438 \u0438\u043C \u0447\u0442\u043E \u0442\u0430\u043A\u043E\u0435 \u0445\u0430\u0440\u0434-\u0431\u0430\u0441\u0441! \u0427\u0438\u043A\u0438-\u0431\u0440\u0438\u043A\u0438 \u0438 \u0432 \u0434\u0430\u043C\u043A\u0438!",   // Ну всё, гопник! Починил тебя! Хватай семечки, водку, и покажи им что такое хард-басс! Чики-брики и в дамки!
            "\u0412\u0441\u0451. \u0411\u043E\u0435\u0441\u043F\u043E\u0441\u043E\u0431\u0435\u043D. \u0412\u043F\u0435\u0440\u0451\u0434.",   // Всё. Боеспособен. Вперёд.
            "\u041F\u043E\u0441\u043B\u0435\u0434\u043D\u0435\u0435. \u0422\u044B \u0432 \u043D\u043E\u0440\u043C\u0435.",   // Последнее. Ты в норме.
            "\u041F\u043E\u0447\u0438\u043D\u0438\u043B. \u0411\u0435\u0440\u0435\u0433\u0438 \u0441\u0435\u0431\u044F, \u0431\u0440\u0430\u0442."   // Починил. Береги себя, брат.
        };

        private static readonly string[] TeamExfilMessagesRU =
        {
            "\u041E\u0442\u0445\u043E\u0434\u0438\u043C. \u0423\u0434\u0430\u0447\u0438, \u0431\u0440\u0430\u0442.",   // Отходим. Удачи, брат.
            "\u0417\u0430\u0434\u0430\u0447\u0443 \u0432\u044B\u043F\u043E\u043B\u043D\u0438\u043B\u0438. \u0423\u0445\u043E\u0434\u0438\u043C.",   // Задачу выполнили. Уходим.
            "\u0420\u0430\u0431\u043E\u0442\u0430 \u0441\u0434\u0435\u043B\u0430\u043D\u0430. \u0411\u044B\u0432\u0430\u0439.",   // Работа сделана. Бывай.
            "\u041E\u0442\u0445\u043E\u0434. \u0414\u0430\u043B\u044C\u0448\u0435 \u0441\u0430\u043C.",   // Отход. Дальше сам.
            "\u0421\u0432\u0430\u043B\u0438\u0432\u0430\u0435\u043C. \u0414\u0430\u0439 \u0438\u043C \u0436\u0430\u0440\u0443!"   // Сваливаем. Дай им жару!
        };

        #endregion

        #region Public Notification Methods

        /// <summary>Notify player that help has been requested.</summary>
        /// <returns>The index of the variant selected (0-4), for audio sync.</returns>
        public static int NotifyHelpRequested(EPlayerSide side)
        {
            var messages = IsRussian(side) ? SummonRequestMessagesRU : SummonRequestMessagesEN;
            return ShowNotification(messages, "summon_request", Color.green);
        }

        /// <summary>Notify player that the team is en route.</summary>
        /// <returns>The index of the variant selected (0-4), for audio sync.</returns>
        public static int NotifyHelpEnRoute(EPlayerSide side)
        {
            var messages = IsRussian(side) ? TeamEnRouteMessagesRU : TeamEnRouteMessagesEN;
            return ShowNotification(messages, "team_enroute", Color.green);
        }

        /// <summary>Notify player that the team has arrived.</summary>
        /// <returns>The index of the variant selected (0-4), for audio sync.</returns>
        public static int NotifyHelpArrived(EPlayerSide side)
        {
            var messages = IsRussian(side) ? TeamArrivedMessagesRU : TeamArrivedMessagesEN;
            return ShowNotification(messages, "team_arrived", Color.green);
        }

        /// <summary>Notify player that healing has started.</summary>
        /// <returns>The index of the variant selected (0-4), for audio sync.</returns>
        public static int NotifyHealingStarted(EPlayerSide side)
        {
            var messages = IsRussian(side) ? HealingStartMessagesRU : HealingStartMessagesEN;
            return ShowNotification(messages, "healing_start", Color.cyan);
        }

        /// <summary>Notify player that healing is complete.</summary>
        /// <returns>The index of the variant selected (0-4), for audio sync.</returns>
        public static int NotifyHealingComplete(EPlayerSide side)
        {
            var messages = IsRussian(side) ? HealingCompleteMessagesRU : HealingCompleteMessagesEN;
            return ShowNotification(messages, "healing_complete", Color.cyan);
        }

        /// <summary>Notify player that the team is exfilling.</summary>
        /// <returns>The index of the variant selected (0-4), for audio sync.</returns>
        public static int NotifyTeamExfilling(EPlayerSide side)
        {
            var messages = IsRussian(side) ? TeamExfilMessagesRU : TeamExfilMessagesEN;
            return ShowNotification(messages, "team_exfil", Color.gray);
        }

        #endregion

        #region Warning Notifications (no audio, always English)

        public static void WarnCooldown(float remainingSeconds)
        {
            ShowWarning($"MedicBuddy on cooldown: {remainingSeconds:F0}s remaining");
        }

        public static void WarnSpawnFailed()
        {
            ShowWarning("MedicBuddy spawn failed. Try again later.");
        }

        public static void NotifyMedicPromoted()
        {
            ShowWarning("Medic is down! Another team member is taking over medical duties.");
        }

        public static void NotifyRallyPointSet()
        {
            ShowWarning("Casualty Collection Point set! Team is rallying to your position.");
        }

        public static void WarnNoActiveTeam()
        {
            ShowWarning("No active MedicBuddy team to rally.");
        }

        public static void WarnNotInjured()
        {
            ShowWarning("You're not injured - MedicBuddy not needed.");
        }

        public static void WarnHasMedicalSupplies()
        {
            ShowWarning("You have medical supplies - heal yourself first.");
        }

        public static void WarnPMCOnly()
        {
            ShowWarning("MedicBuddy is only available in PMC raids.");
        }

        public static void NotifyTeamCarriesMeds()
        {
            ShowWarning("Team members carry medical supplies - check their gear if needed.");
        }

        #endregion

        #region Private Helpers

        /// <summary>BEAR and Scav (Savage) factions use Russian; USEC uses English.</summary>
        private static bool IsRussian(EPlayerSide side)
        {
            return side != EPlayerSide.Usec;
        }

        /// <summary>
        /// Selects a random message variant and displays it as a toast notification.
        /// Returns the variant index so audio can play the matching voice line.
        /// </summary>
        private static int ShowNotification(string[] messages, string eventName, Color color)
        {
            int index = _random.Next(messages.Length);
            string message = messages[index];

            try
            {
                NotificationManagerClass.DisplayMessageNotification(
                    message,
                    ENotificationDurationType.Default,
                    ENotificationIconType.Default,
                    color);
            }
            catch (Exception ex)
            {
                // Fallback to log if notification system unavailable
                BotMindPlugin.Log?.LogInfo($"[MedicBuddy] {message}");
                BotMindPlugin.Log?.LogDebug($"Notification display failed for {eventName}: {ex.Message}");
            }

            return index;
        }

        private static void ShowWarning(string message)
        {
            try
            {
                NotificationManagerClass.DisplayMessageNotification(
                    message,
                    ENotificationDurationType.Default,
                    ENotificationIconType.Alert,
                    Color.yellow);
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogInfo($"[MedicBuddy] {message}");
                BotMindPlugin.Log?.LogDebug($"Warning notification failed: {ex.Message}");
            }
        }

        #endregion
    }
}
