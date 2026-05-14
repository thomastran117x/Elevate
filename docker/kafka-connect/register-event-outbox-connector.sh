#!/bin/sh
set -eu

CONNECT_URL="${KAFKA_CONNECT_URL:-http://kafka-connect:8083}"
EVENT_CONNECTOR_NAME="event-search-outbox"
EVENT_CONFIG_PATH="/scripts/event-outbox-connector.json"
CLUB_CONNECTOR_NAME="club-search-outbox"
CLUB_CONFIG_PATH="/scripts/club-outbox-connector.json"

echo "Waiting for Kafka Connect at ${CONNECT_URL}..."
until curl -fsS "${CONNECT_URL}/connectors" >/dev/null 2>&1; do
  sleep 5
done

echo "Registering connector ${EVENT_CONNECTOR_NAME}..."
curl -fsS \
  -X PUT \
  -H "Content-Type: application/json" \
  "${CONNECT_URL}/connectors/${EVENT_CONNECTOR_NAME}/config" \
  --data @"${EVENT_CONFIG_PATH}"

echo "Connector ${EVENT_CONNECTOR_NAME} is configured."

echo "Registering connector ${CLUB_CONNECTOR_NAME}..."
curl -fsS \
  -X PUT \
  -H "Content-Type: application/json" \
  "${CONNECT_URL}/connectors/${CLUB_CONNECTOR_NAME}/config" \
  --data @"${CLUB_CONFIG_PATH}"

echo "Connector ${CLUB_CONNECTOR_NAME} is configured."
