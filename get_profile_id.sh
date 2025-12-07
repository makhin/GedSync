#!/bin/bash
# Скрипт для получения числового ID профиля Geni

TOKEN=$(jq -r '.AccessToken' geni_token.json)

if [ -z "$1" ]; then
    echo "Использование: $0 <GUID или URL профиля>"
    echo "Примеры:"
    echo "  $0 6000000206529622827"
    echo "  $0 https://www.geni.com/people/Name/6000000206529622827"
    echo ""
    echo "Или без параметра для получения вашего профиля:"
    exit 1
fi

# Извлечь GUID из URL если передан URL
GUID=$1
if [[ $GUID == *"geni.com"* ]]; then
    GUID=$(echo "$GUID" | grep -oP '\d{19}$' || echo "$GUID")
fi

echo "Получение профиля..."
RESPONSE=$(curl -s -H "Authorization: Bearer $TOKEN" "https://www.geni.com/api/profile")

if [[ $RESPONSE == *"error"* ]]; then
    echo "Ошибка: $RESPONSE"
    exit 1
fi

# Извлечь числовой ID
NUMERIC_ID=$(echo "$RESPONSE" | jq -r '.id' | sed 's/profile-//')
GUID_FROM_API=$(echo "$RESPONSE" | jq -r '.guid')
NAME=$(echo "$RESPONSE" | jq -r '.name')

echo ""
echo "=== Ваш профиль ==="
echo "Имя: $NAME"
echo "Числовой ID: $NUMERIC_ID"
echo "GUID: $GUID_FROM_API"
echo ""
echo "Используйте числовой ID для команды sync:"
echo "  --anchor-geni $NUMERIC_ID"

