FROM python:3.12-slim

WORKDIR /app

ENV PYTHONUNBUFFERED=1 \
    PYTHONDONTWRITEBYTECODE=1 \
    OPTIMO_WORKER_ROOT=/data/worker_runs

COPY requirements.txt /app/requirements.txt
RUN pip install --no-cache-dir -r /app/requirements.txt

COPY main.py /app/main.py
COPY __init__.py /app/__init__.py

EXPOSE 1112

CMD ["uvicorn", "main:app", "--host", "0.0.0.0", "--port", "1112"]
