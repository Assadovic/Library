#!/bin/make
CC = gcc
CXX = g++
LDFLAGS = -lm

SRCS = Unsafe.cpp
OBJS := $(SRCS:.cpp=.o)

TARGET = Library.so

.PHONY: all clean
.SUFFIXES: .c .cpp .o

.c.o:
	$(CC) $(CFLAGS) -o $@ -c $^
.cpp.o:
	$(CXX) $(CXXFLAGS) -o $@ -c $^
.c:
	$(CC) $(CFLAGS) -o $@ $^ $(LDFLAGS)
.cpp:
	$(CXX) $(CXXFLAGS) -o $@ $^ $(LDFLAGS)

$(TARGET): $(OBJS)
	mkdir -p $(BUILD)
	$(CXX) $(CXXFLAGS) -o $(BUILD)/$(TARGET) $(OBJS)
	rm $(OBJS)

clean:
	rm -R $(BUILD)
	rm $(OBJS)
