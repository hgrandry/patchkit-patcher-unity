﻿using System;
using JetBrains.Annotations;
using PatchKit.Logging;

namespace PatchKit.Unity.Patcher.Debug
{
    public class UnityMessageWriter : IMessagesStreamObserver
    {
        [NotNull]
        private readonly IMessageFormatter _formatter;

        public UnityMessageWriter([NotNull] IMessageFormatter formatter)
        {
            _formatter = formatter;
        }

        public void OnNext(Message message, MessageContext messageContext)
        {
            var text = _formatter.Format(message, messageContext);

            switch (message.Type)
            {
                case MessageType.Trace:
                    UnityEngine.Debug.Log(text);
                    break;
                case MessageType.Debug:
                    UnityEngine.Debug.Log(text);
                    break;
                case MessageType.Warning:
                    UnityEngine.Debug.LogWarning(text);
                    break;
                case MessageType.Error:
                    UnityEngine.Debug.LogError(text);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}