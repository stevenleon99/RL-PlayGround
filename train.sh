#!/usr/bin/env bash
set -euo pipefail

# Unity connection.
# Leave UNITY_FILE empty to connect to the Unity Editor while it is in Play mode.
UNITY_FILE="${UNITY_FILE:-}"
WORKER_ID="${WORKER_ID:-0}"
SEED="${SEED:-42}"
TIME_SCALE="${TIME_SCALE:-1.0}"
TIMEOUT_WAIT="${TIMEOUT_WAIT:-120}"
BEHAVIOR_NAME="${BEHAVIOR_NAME:-Drone}"

# Training length.
TOTAL_TIMESTEPS="${TOTAL_TIMESTEPS:-100000}"
MAX_EPISODE_STEPS="${MAX_EPISODE_STEPS:-1000}"

# DQN hyperparameters.
LEARNING_RATE="${LEARNING_RATE:-0.0001}"
BUFFER_SIZE="${BUFFER_SIZE:-50000}"
LEARNING_STARTS="${LEARNING_STARTS:-1000}"
BATCH_SIZE="${BATCH_SIZE:-64}"
TAU="${TAU:-1.0}"
GAMMA="${GAMMA:-0.99}"
TRAIN_FREQ="${TRAIN_FREQ:-4}"
GRADIENT_STEPS="${GRADIENT_STEPS:-1}"
TARGET_UPDATE_INTERVAL="${TARGET_UPDATE_INTERVAL:-1000}"
EXPLORATION_FRACTION="${EXPLORATION_FRACTION:-0.2}"
EXPLORATION_INITIAL_EPS="${EXPLORATION_INITIAL_EPS:-1.0}"
EXPLORATION_FINAL_EPS="${EXPLORATION_FINAL_EPS:-0.05}"
NET_ARCH="${NET_ARCH:-256,256}"
DEVICE="${DEVICE:-auto}"

# Discrete action table values.
# DQN chooses one discrete action, then train_dqn.py maps it to:
# [thrust, yaw, forward, right].
HOVER_THRUST="${HOVER_THRUST:-0.654}"
THRUST_DELTA="${THRUST_DELTA:-0.25}"
MOVE_AMOUNT="${MOVE_AMOUNT:-0.6}"
YAW_AMOUNT="${YAW_AMOUNT:-0.5}"

# Outputs.
LOG_DIR="${LOG_DIR:-runs/dqn}"
MODEL_PATH="${MODEL_PATH:-models/drone_dqn}"

ARGS=(
  --worker-id "$WORKER_ID"
  --seed "$SEED"
  --time-scale "$TIME_SCALE"
  --timeout-wait "$TIMEOUT_WAIT"
  --behavior-name "$BEHAVIOR_NAME"
  --total-timesteps "$TOTAL_TIMESTEPS"
  --max-episode-steps "$MAX_EPISODE_STEPS"
  --learning-rate "$LEARNING_RATE"
  --buffer-size "$BUFFER_SIZE"
  --learning-starts "$LEARNING_STARTS"
  --batch-size "$BATCH_SIZE"
  --tau "$TAU"
  --gamma "$GAMMA"
  --train-freq "$TRAIN_FREQ"
  --gradient-steps "$GRADIENT_STEPS"
  --target-update-interval "$TARGET_UPDATE_INTERVAL"
  --exploration-fraction "$EXPLORATION_FRACTION"
  --exploration-initial-eps "$EXPLORATION_INITIAL_EPS"
  --exploration-final-eps "$EXPLORATION_FINAL_EPS"
  --net-arch "$NET_ARCH"
  --device "$DEVICE"
  --hover-thrust "$HOVER_THRUST"
  --thrust-delta "$THRUST_DELTA"
  --move-amount "$MOVE_AMOUNT"
  --yaw-amount "$YAW_AMOUNT"
  --log-dir "$LOG_DIR"
  --model-path "$MODEL_PATH"
)

if [[ -n "$UNITY_FILE" ]]; then
  ARGS+=(--unity-file "$UNITY_FILE")
fi

python train_dqn.py "${ARGS[@]}"
