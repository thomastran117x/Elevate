#!/bin/sh
set -eu

CONNECT_URL="${KAFKA_CONNECT_URL:-http://kafka-connect:8083}"
CONNECTOR_NAME="event-search-outbox"
CONFIG_PATH="/scripts/event-outbox-connector.json"

echo "Waiting for Kafka Connect at ${CONNECT_URL}..."
until curl -fsS "${CONNECT_URL}/connectors" >/dev/null 2>&1; do
  sleep 5
done

echo "Registering connector ${CONNECTOR_NAME}..."
curl -fsS \
  -X PUT \
  -H "Content-Type: application/json" \
  "${CONNECT_URL}/connectors/${CONNECTOR_NAME}/config" \
  --data @"${CONFIG_PATH}"

echo "Connector ${CONNECTOR_NAME} is configured."
